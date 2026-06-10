using System.Buffers.Binary;
using System.Net.Sockets;
using AlexWoW.Cryptography;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.DataStores.Terrain;
using AlexWoW.WorldServer.Handlers;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Net;

/// <summary>
/// Контекст одного world-соединения: транспорт (сокет, RC4, фрейминг) + состояние сессии.
/// Логику опкодов держат классы в Handlers/, получая эту сессию как контекст.
/// Заголовки (3.3.5a): сервер→клиент 2b size(BE)+2b opcode(LE); клиент→сервер 2b size(BE)+4b opcode(LE);
/// size включает длину opcode. После handshake заголовки шифруются RC4, тело — открытым текстом.
/// </summary>
public sealed class WorldSession
{
    private const int ServerHeaderSize = 4; // 2 size + 2 opcode
    private const int ClientHeaderSize = 6; // 2 size + 4 opcode

    private readonly NetworkStream _stream;
    private readonly WorldHeaderCrypt _crypt = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1); // сериализация отправки (RC4 — stateful)

    public WorldSession(Socket socket, IAccountRepository database, ICharacterRepository characters,
        IInventoryRepository items, IQuestRepository quests, ICharacterStateRepository charState,
        IWorldRepository worldDatabase, TerrainMaps terrain, WorldState world, WorldServerOptions options, ILogger logger)
    {
        _stream = new NetworkStream(socket, ownsSocket: true);
        RemoteIp = (socket.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "?";
        Database = database;
        Characters = characters;
        Items = items;
        Quests = quests;
        CharState = charState;
        WorldDb = worldDatabase;
        Terrain = terrain;
        World = world;
        Options = options;
        Logger = logger;
    }

    // --- Контекст для обработчиков ---
    internal IAccountRepository Database { get; }
    internal ICharacterRepository Characters { get; }
    internal IInventoryRepository Items { get; }
    internal IQuestRepository Quests { get; }
    internal ICharacterStateRepository CharState { get; }
    internal IWorldRepository WorldDb { get; }
    internal TerrainMaps Terrain { get; }
    internal WorldState World { get; }
    internal WorldServerOptions Options { get; }
    internal ILogger Logger { get; }
    internal string RemoteIp { get; }

    // --- Состояние сессии ---
    internal uint AuthSeed { get; set; }
    internal string? Account { get; set; }
    internal uint AccountId { get; set; }
    /// <summary>Аккаунт — администратор (доступ к дев/GM-командам). Ставится при auth. M7.</summary>
    internal bool IsAdmin { get; set; }
    internal uint InWorldGuid { get; set; } // != 0, пока персонаж в мире
    internal float PosX { get; set; }
    internal float PosY { get; set; }
    internal float PosZ { get; set; }
    internal float PosO { get; set; }

    /// <summary>Данные персонажа в мире (заданы после CMSG_PLAYER_LOGIN). M5.</summary>
    internal Character? Character { get; set; }

    /// <summary>Представление в реестре мира, пока персонаж в мире (null вне мира). M5.</summary>
    internal WorldPlayer? Player { get; set; }

    /// <summary>Инвентарь персонажа в мире (предметы во всех слотах). Загружается при входе. M6.1.</summary>
    internal List<InventoryItem> Inventory { get; } = new();

    /// <summary>Кэш class/ContainerSlots/MaxDurability по entry предметов инвентаря (батч при входе) —
    /// чтобы знать, какие предметы суммки (контейнеры), без запроса БД на каждый предмет. M6.13.</summary>
    internal Dictionary<uint, ItemBagInfo> ItemBagInfo { get; } = new();

    /// <summary>Деньги персонажа (медь) в мире. Загружается при входе, меняется торговлей. M6.2.</summary>
    internal uint Money { get; set; }

    /// <summary>
    /// Существа (NPC), показанные клиенту этой сессии (guid → авторитетная сущность). M5/M6.3.
    /// Потокобезопасный: пишет поток сессии (видимость), читает поток тика (рассылка боя/HP).
    /// </summary>
    internal System.Collections.Concurrent.ConcurrentDictionary<ulong, WorldCreature> VisibleNpcs { get; } = new();

    /// <summary>Гейм-объекты, показанные клиенту этой сессии (guid → спавн). M5.6b.</summary>
    internal Dictionary<ulong, GoSpawn> VisibleGos { get; } = new();

    /// <summary>
    /// Реестр dev-сущностей-существ этой сессии (слот → guid): класс-тренер/проф-тренер/вендор реагентов.
    /// Per-session (привязаны к месту/виду игрока): replace по слоту, снятие через <c>.devclean</c>, и
    /// «липкость» в видимости (см. <see cref="IsDevNpc"/>) — не сносятся при ходьбе. D1.
    /// </summary>
    internal Dictionary<string, ulong> DevNpcs { get; } = new();

    /// <summary>Является ли NPC dev-сущностью этой сессии — чтобы пересчёт видимости не слал DESTROY. D1.</summary>
    internal bool IsDevNpc(ulong guid) => DevNpcs.Count > 0 && DevNpcs.ContainsValue(guid);

    /// <summary>
    /// Реестр dev-гейм-объектов этой сессии (слот → guid): крафт-станки/почта (<c>.craft</c>). Спавнятся
    /// прямой посылкой и НЕ кладутся в <see cref="VisibleGos"/> — пересчёт видимости их не трогает (липкость
    /// «бесплатно»). Снимаются через <c>.craft off</c>/<c>.devclean</c>. D3.
    /// </summary>
    internal Dictionary<string, ulong> DevGos { get; } = new();

    /// <summary>
    /// Другие игроки, показанные клиенту этой сессии (set guid'ов). Доступ из нескольких потоков
    /// (сосед спавнит нас из своего потока) — потокобезопасный. Динамическая видимость игроков (M6).
    /// </summary>
    internal System.Collections.Concurrent.ConcurrentDictionary<ulong, byte> VisiblePlayers { get; } = new();

    /// <summary>Позиция последнего пересчёта видимости NPC (троттлинг по дистанции). M5.6.</summary>
    internal float LastVisX { get; set; }
    internal float LastVisY { get; set; }

    /// <summary>Текущая цель (CMSG_SET_SELECTION). 0 — нет. M6.3.</summary>
    internal ulong SelectionGuid { get; set; }

    /// <summary>Авторитетное здоровье игрока (UNIT_FIELD_HEALTH). Меняется уроном существ. M6.7.</summary>
    internal uint Health { get; set; }
    internal uint MaxHealth { get; set; }
    /// <summary>Текущий опыт на уровне (PLAYER_XP). Прокачка M9.1.</summary>
    internal uint Xp { get; set; }
    /// <summary>Время последней боевой активности (нанёс/получил урон) — для внебоевого регена HP. M6.7.</summary>
    internal long LastCombatMs { get; set; }
    /// <summary>Время последнего тика регена HP (кадэнс 1 с). M6.7.</summary>
    internal long LastHealthRegenMs { get; set; }
    /// <summary>Игрок мёртв (HP=0, ждёт release/возрождения). M6.7.</summary>
    internal bool IsDead { get; set; }

    /// <summary>GUID трупа с открытым окном лута (0 — окно закрыто). M6.6.</summary>
    internal ulong LootGuid { get; set; }

    /// <summary>Счётчик телепортов (movement order counter в MSG_MOVE_TELEPORT_ACK). M7 #33.</summary>
    private uint _teleportCounter;
    internal uint NextTeleportCounter() => ++_teleportCounter;

    /// <summary>Журнал квестов: слот (0..24) → прогресс (null — пусто). Персист — позже. M6.5.</summary>
    internal World.QuestProgress?[] QuestSlots { get; } = new World.QuestProgress?[Protocol.UpdateField.QuestLogSlots];
    /// <summary>Сданные квесты (для предусловий PrevQuestId и анти-повтора). Персист — позже. M6.5.</summary>
    internal HashSet<uint> CompletedQuests { get; } = new();

    /// <summary>GUID существа, по которому идёт авто-атака (0 — не в бою). Читается тиком. M6.3.</summary>
    internal ulong CombatTargetGuid { get; set; }

    /// <summary>Время следующего мили-свинга (<see cref="Environment.TickCount64"/>, мс). M6.3.</summary>
    internal long NextMeleeSwingMs { get; set; }

    /// <summary>Послали ли клиенту «вне радиуса» для текущего эпизода (анти-спам). M6.3.</summary>
    internal bool MeleeNotInRangeNotified { get; set; }

    // --- Синхронизация часов (M6.3 ч.2: нормализация времени движения) ---
    /// <summary>Следующий счётчик для SMSG_TIME_SYNC_REQ.</summary>
    internal uint TimeSyncCounter { get; set; }
    /// <summary>Счётчик последнего отправленного REQ (для матчинга RESP).</summary>
    internal uint TimeSyncOutstanding { get; set; }
    /// <summary>Серверное время (32-бит мс) отправки последнего REQ.</summary>
    internal long TimeSyncSentMs { get; set; }
    /// <summary>Серверное время (TickCount64) последней рассылки REQ — для периодичности.</summary>
    internal long LastTimeSyncDispatchMs { get; set; }
    /// <summary>Дельта часов: <c>serverMs − clientTicks</c>. null — пока не синхронизировано.</summary>
    internal long? ClockDeltaMs { get; set; }

    // --- Каст спелла (M6.4) ---
    /// <summary>Спелл, который сейчас кастуется (0 — нет каста). Завершается в тике.</summary>
    internal uint CastingSpellId { get; set; }
    // cast_count и target теперь протаскиваются параметрами каста (не через сессию) — повторное нажатие
    // во время каста не перезатирает счётчик завершаемого каста (M10.4a фикс залипания завершения).
    /// <summary>Позиция в начале каста — для прерывания при сдвиге (движение, не поворот). M6.4.</summary>
    internal float CastStartX { get; set; }
    internal float CastStartY { get; set; }
    /// <summary>Поколение каста: инкремент на каждый каст; отложенное завершение проверяет совпадение
    /// (чтобы не завершить отменённый/перебитый каст). M6.4.</summary>
    internal int CastGeneration { get; set; }
    /// <summary>Момент окончания глобального кулдауна (GCD, <see cref="Environment.TickCount64"/>, мс). M10.3.</summary>
    internal long GcdEndMs { get; set; }

    /// <summary>Текущая/макс. мана (UNIT_FIELD_POWER1). MaxMana=0 — класс без маны (rage/energy):
    /// расход не применяется. Инициализируется при входе в мир. M6.4 инкремент 2.</summary>
    internal uint Mana { get; set; }
    internal uint MaxMana { get; set; }
    /// <summary>Время последнего успешного каста — «правило 5 секунд» (реген маны паузится). M6.4.</summary>
    internal long LastSpellCastMs { get; set; }
    /// <summary>Время последнего тика регена маны (кадэнс 1 с). M6.4.</summary>
    internal long LastManaRegenMs { get; set; }

    // --- Боевые ресурсы: ярость/энергия (M6.12) ---
    /// <summary>Ярость воина/друида (UNIT_FIELD_POWER1+1). Хранится ×10 (0..1000 = 0..100 у клиента).
    /// Копится от мили-урона, распадается вне боя. 0 у не-ярость-классов. M6.12.</summary>
    internal uint Rage { get; set; }
    /// <summary>Энергия разбойника (UNIT_FIELD_POWER1+3), 0..100. Реген ~постоянный. M6.12.</summary>
    internal uint Energy { get; set; }
    /// <summary>Скорость оружия главной руки (мс) — для формулы ярости. Ставится в RefreshMeleeAsync. M6.12.</summary>
    internal uint MainHandSpeedMs { get; set; } = 2000;
    /// <summary>Урон оружия главной руки (min/max) — для мили-абилок (WEAPON_DAMAGE). RefreshMeleeAsync. M10.4a.</summary>
    internal float WeaponMinDamage { get; set; } = 1f;
    internal float WeaponMaxDamage { get; set; } = 2f;
    /// <summary>Время последнего тика ресурса (реген энергии / распад ярости, кадэнс 1 с). M6.12.</summary>
    internal long LastResourceTickMs { get; set; }
    /// <summary>Кулдауны спеллов: spellId → момент готовности (<see cref="Environment.TickCount64"/>, мс). M6.4.</summary>
    internal System.Collections.Generic.Dictionary<uint, long> SpellCooldowns { get; } = new();

    /// <summary>Известные игроку спеллы (стартовые по классу + языковые + изученные у тренера). Для
    /// HasSpell-проверок тренера и анти-дубля. Загружается при входе в мир. M9.3.</summary>
    internal HashSet<uint> KnownSpells { get; } = new();

    /// <summary>Свободные очки талантов (PLAYER_CHARACTER_POINTS1). Вычисляются: MaxPoints(level) − потрачено. M9.6.</summary>
    internal uint TalentPoints { get; set; }
    /// <summary>Изученные таланты: talentId → ранг (0-индексный). Загружается при входе. M9.6/M9.7.</summary>
    internal Dictionary<uint, byte> LearnedTalents { get; } = new();

    /// <summary>Активные ауры (баффы/дебаффы/формы). Слот = позиция в баф-баре. M6.11.</summary>
    internal List<World.ActiveAura> Auras { get; } = new();
    /// <summary>Активные периодические эффекты этого кастера (DoT на существах / HoT на себе). M10.4b.</summary>
    internal List<Handlers.PeriodicEffect> Periodics { get; } = new();
    /// <summary>Текущая форма шейпшифта (стойка воина/форма друида); 0 — нет формы. UNIT_FIELD_BYTES_2 байт 3. M6.11.</summary>
    internal byte ShapeshiftForm { get; set; }

    internal void InitCrypt(byte[] sessionKey) => _crypt.Init(sessionKey);

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await AuthHandlers.SendAuthChallengeAsync(this, ct);

            while (!ct.IsCancellationRequested)
            {
                var packet = await ReadPacketAsync(ct);
                if (packet is null)
                    break;
                await WorldPacketRouter.DispatchAsync(this, packet.Value, ct);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException
                                   or InvalidOperationException or EndOfStreamException)
        {
            Logger.LogDebug("World-соединение {Ip} закрыто: {Message}", RemoteIp, ex.Message);
        }
        finally
        {
            await SavePositionIfInWorldAsync(CancellationToken.None);
            await LeaveWorldAsync(CancellationToken.None);
            await _stream.DisposeAsync();
        }
    }

    /// <summary>Убирает персонажа из мира (DESTROY соседям + снятие с реестра). Идемпотентно.</summary>
    internal async Task LeaveWorldAsync(CancellationToken ct)
    {
        var player = Player;
        if (player is null)
            return;
        // M10.5: сохранить временны́е баффы/HoT с остатком длительности ДО очистки (InWorldGuid ещё валиден).
        await Handlers.Auras.SaveTimedAurasAsync(this, InWorldGuid, ct);
        Player = null;
        InWorldGuid = 0;
        CombatTargetGuid = 0; // M6.3: вне мира боя нет
        SelectionGuid = 0;
        IsDead = false;       // M6.7: боевое/жизненное состояние сбрасывается при выходе
        LootGuid = 0;         // M6.6: окно лута закрыто
        Array.Clear(QuestSlots); // M6.5: журнал квестов (в памяти) сбрасывается при выходе
        CompletedQuests.Clear();
        CastingSpellId = 0;   // M6.4: каст прерывается при выходе
        SpellCooldowns.Clear();
        KnownSpells.Clear();  // M9.3: набор спеллов перезагружаем при следующем входе
        LearnedTalents.Clear(); // M9.6: таланты перезагружаем при следующем входе
        Auras.Clear();        // M6.11: ауры сбрасываются при выходе (клиент пересоздаст при входе)
        Periodics.Clear();    // M10.4b: периодические эффекты (DoT/HoT)
        ShapeshiftForm = 0;
        foreach (var guid in DevNpcs.Values) // D1: снять dev-сущности с глобального реестра существ
            World.RemoveCreature(guid);
        DevNpcs.Clear();
        DevGos.Clear();      // D3: dev-станки (клиент выгрузил мир)
        VisibleNpcs.Clear(); // клиент выгрузил мир — при повторном входе пересоздаём с нуля
        VisibleGos.Clear();
        VisiblePlayers.Clear();
        Inventory.Clear();
        ItemBagInfo.Clear(); // M6.13: кэш сумок перезагружается при следующем входе
        try
        {
            await World.LeaveWorldAsync(player, ct);
        }
        catch (Exception ex)
        {
            Logger.LogDebug("LeaveWorld '{User}': {Msg}", Account, ex.Message);
        }
    }

    /// <summary>Сохраняет позицию персонажа, если он в мире (логаут/разрыв).</summary>
    internal async Task SavePositionIfInWorldAsync(CancellationToken ct)
    {
        if (InWorldGuid == 0)
            return;
        try
        {
            await Characters.SavePositionAsync(InWorldGuid, PosX, PosY, PosZ, ct);
            Logger.LogInformation("Позиция '{User}' сохранена: ({X};{Y};{Z})", Account, PosX, PosY, PosZ);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Не удалось сохранить позицию '{User}': {Msg}", Account, ex.Message);
        }
    }

    /// <summary>Отправляет пакет клиенту (заголовок шифруется, если RC4 инициализирован).</summary>
    internal async Task SendAsync(WorldOpcode opcode, byte[] body, CancellationToken ct)
    {
        var header = new byte[ServerHeaderSize];
        var size = (ushort)(body.Length + 2); // +2 байта opcode
        header[0] = (byte)(size >> 8);          // size big-endian
        header[1] = (byte)(size & 0xFF);
        header[2] = (byte)((uint)opcode & 0xFF); // opcode little-endian (2 байта)
        header[3] = (byte)(((uint)opcode >> 8) & 0xFF);

        // Сериализуем отправку: соседние сессии шлют этому клиенту из своих потоков,
        // а RC4 (_crypt) — потоковый шифр с общим состоянием. Шифрование + запись — под локом.
        await _sendLock.WaitAsync(ct);
        try
        {
            if (_crypt.IsInitialized)
                _crypt.Encrypt(header);

            await _stream.WriteAsync(header, ct);
            if (body.Length > 0)
                await _stream.WriteAsync(body, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<IncomingPacket?> ReadPacketAsync(CancellationToken ct)
    {
        var header = new byte[ClientHeaderSize];
        var read = await _stream.ReadAsync(header.AsMemory(0, 1), ct);
        if (read == 0)
            return null; // соединение закрыто

        await _stream.ReadExactlyAsync(header.AsMemory(1, ClientHeaderSize - 1), ct);

        if (_crypt.IsInitialized)
            _crypt.Decrypt(header);

        var size = (ushort)((header[0] << 8) | header[1]); // big-endian; включает 4 байта opcode
        var opcode = (WorldOpcode)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2, 4));
        var bodyLength = size - 4;
        if (bodyLength < 0 || bodyLength > 0x10000)
            throw new InvalidOperationException($"Некорректная длина пакета: {bodyLength}");

        var body = new byte[bodyLength];
        if (bodyLength > 0)
            await _stream.ReadExactlyAsync(body, ct);

        return new IncomingPacket(opcode, body);
    }
}
