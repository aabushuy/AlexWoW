using System.Buffers.Binary;
using System.Net.Sockets;
using AlexWoW.Cryptography;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Handlers;
using AlexWoW.WorldServer.Net.SessionState;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Net;

/// <summary>
/// Контекст одного world-соединения: транспорт (сокет, RC4, фрейминг) + состояние сессии.
/// Логику опкодов держат классы в Handlers/, получая эту сессию как контекст.
/// Состояние сгруппировано по областям в компоненты <c>Net/SessionState</c> (Combat/Cast/Visibility/
/// Quest/Progression/Inv) — у самой сессии остаются транспорт, идентичность, присутствие в мире и
/// синхронизация часов (M7 S9 #43).
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
    private readonly WorldSessionServices _services;

    internal WorldSession(Socket socket, WorldSessionServices services)
    {
        _stream = new NetworkStream(socket, ownsSocket: true);
        RemoteIp = (socket.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "?";
        _services = services;
    }

    // --- Контекст для обработчиков (M7 S9 #43: мосты репозиториев сняты — модули/сервисы/dev-команды
    // получают репозитории ctor-инъекцией; через сессию остаются только мир, опции и логгер by design) ---
    internal WorldState World => _services.World;
    internal WorldServerOptions Options => _services.Options;
    internal ILogger Logger => _services.Logger;
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

    // --- Компоненты состояния сессии (M7 S9 #43: плоские поля сгруппированы по областям, SRP) ---
    /// <summary>Боевое состояние: цель, HP, мили, ярость/энергия. M7 S9 #43.</summary>
    internal SessionCombatState Combat { get; } = new();
    /// <summary>Состояние каста: текущий каст, GCD, мана, кулдауны. M7 S9 #43.</summary>
    internal SessionCastState Cast { get; } = new();
    /// <summary>Видимость: показанные NPC/GO/игроки, dev-сущности. M7 S9 #43.</summary>
    internal SessionVisibilityState Visibility { get; } = new();
    /// <summary>Квесты: журнал и сданные. M7 S9 #43.</summary>
    internal SessionQuestState Quest { get; } = new();
    /// <summary>Прогрессия: опыт, спеллы, таланты, навыки, ауры. M7 S9 #43.</summary>
    internal SessionProgressionState Progression { get; } = new();
    /// <summary>Инвентарь: предметы, сумки, деньги, лут. M7 S9 #43.</summary>
    internal SessionInventoryState Inv { get; } = new();

    /// <summary>Счётчик телепортов (movement order counter в MSG_MOVE_TELEPORT_ACK). M7 #33.</summary>
    private uint _teleportCounter;
    internal uint NextTeleportCounter() => ++_teleportCounter;

    /// <summary>Идёт кросс-карта телепорт: отправлен SMSG_NEW_WORLD, ждём MSG_MOVE_WORLDPORT_ACK. #79.</summary>
    internal bool PendingWorldport { get; set; }

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

    internal void InitCrypt(byte[] sessionKey) => _crypt.Init(sessionKey);

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await _services.AuthChallenge.SendAsync(this, ct);

            while (!ct.IsCancellationRequested)
            {
                var packet = await ReadPacketAsync(ct);
                if (packet is null)
                    break;
                await _services.Router.DispatchAsync(this, packet.Value, ct);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException
                                   or InvalidOperationException or EndOfStreamException)
        {
            Logger.LogDebug(ex, "World-соединение {Ip} закрыто: {Message}", RemoteIp, ex.Message);
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
        await _services.AuraPersistence.SaveTimedAurasAsync(this, InWorldGuid, ct);
        // M12 Spell QA: закрыть незавершённую сессию захвата (тестировщик вышел, не нажав .spelltest stop).
        await _services.SpellTestCapture.StopAsync(this, ct);
        // GROUP.T2: пометить offline в группе (если в ней) + разослать PARTY_MEMBER_STATS.
        try { await _services.GroupSync.MarkOfflineAsync(player, ct); }
        catch (Exception ex) { Logger.LogDebug(ex, "GroupSync MarkOffline '{User}': {Msg}", Account, ex.Message); }
        Player = null;
        InWorldGuid = 0;
        // M7 S9 #43: пер-полевые сбросы перенесены в Reset() компонентов (семантика сохранена 1:1).
        Combat.Reset();      // M6.3/M6.7: цель/бой/смерть
        Quest.Reset();       // M6.5: журнал квестов (в памяти)
        Cast.Reset();        // M6.4: каст/кулдауны
        Progression.Reset(); // M9.3/M9.6/M11.1/M6.11/M10.4b: спеллы/таланты/навыки/ауры
        foreach (var guid in Visibility.DevNpcs.Values) // D1: снять dev-сущности с глобального реестра существ
            World.RemoveCreature(guid);
        Visibility.Reset();  // D1/D3/M5.6: dev-реестры и видимые NPC/GO/игроки
        Inv.Reset();         // M6.6/M6.13: окно лута, инвентарь, кэш сумок
        try
        {
            await World.LeaveWorldAsync(player, ct);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "LeaveWorld '{User}': {Msg}", Account, ex.Message);
        }
    }

    /// <summary>Сохраняет позицию персонажа, если он в мире (логаут/разрыв).</summary>
    internal async Task SavePositionIfInWorldAsync(CancellationToken ct)
    {
        if (InWorldGuid == 0)
            return;
        try
        {
            await _services.Characters.SavePositionAsync(InWorldGuid, PosX, PosY, PosZ, Character?.Map ?? 0, ct);
            Logger.LogInformation("Позиция '{User}' сохранена: ({X};{Y};{Z})", Account, PosX, PosY, PosZ);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Не удалось сохранить позицию '{User}': {Msg}", Account, ex.Message);
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
