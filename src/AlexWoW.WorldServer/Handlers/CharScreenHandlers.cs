using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>Экран персонажей (M3): enum/create/delete, запросы имени/времени, realm split, account data.
/// (DI-модуль, M7 #36.)</summary>
internal sealed class CharScreenHandlers(
    ICharacterRepository characters,
    IInventoryRepository items,
    IWorldRepository worldDb,
    ICharacterStateRepository charState,
    StartingGearService startingGear) : IOpcodeHandlerModule
{
    [WorldOpcodeHandler(WorldOpcode.CmsgCharEnum)]
    public async Task OnCharEnum(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var list = await characters.GetByAccountAsync(session.AccountId, ct);
        var equipment = await BuildPaperdollAsync(session, list, ct);

        // M7 #16: персонажи с заданными склонениями → флаг CHARACTER_FLAG_DECLINED в enum,
        // иначе ruRU-клиент показывает диалог склонений при каждом заходе на экран выбора.
        IReadOnlySet<uint> declined;
        try { declined = await characters.GetGuidsWithDeclinedNamesAsync([.. list.Select(c => c.Guid)], ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "CHAR_ENUM declined-флаги: {Msg}", ex.Message);
            declined = new HashSet<uint>();
        }

        await session.SendAsync(WorldOpcode.SmsgCharEnum, CharEnum.BuildBody(list, equipment, declined), ct);
        session.Logger.LogInformation("CHAR_ENUM: {Count} персонажей для '{User}' (со склонениями: {Decl})",
            list.Count, session.Account, declined.Count);
    }

    /// <summary>
    /// Экипировка для paperdoll на экране выбора: по каждому персонажу слот(0..18) → displayId+invType.
    /// При недоступности БД мира — пустой набор (персонажи всё равно отображаются).
    /// </summary>
    private async Task<IReadOnlyDictionary<uint, CharEnum.SlotDisplay[]>> BuildPaperdollAsync(
        WorldSession session, IReadOnlyList<Character> charList, CancellationToken ct)
    {
        var equippedByChar = new Dictionary<uint, List<InventoryItem>>();
        var entries = new HashSet<uint>();
        foreach (var c in charList)
        {
            var charItems = await items.GetItemsAsync(c.Guid, ct);
            var equipped = charItems.Where(i => i.Bag == InventorySlots.MainBag
                && i.Slot < InventorySlots.EquipmentEnd).ToList();
            if (equipped.Count == 0)
                continue;
            equippedByChar[c.Guid] = equipped;
            foreach (var i in equipped)
                entries.Add(i.ItemEntry);
        }

        if (entries.Count == 0)
            return new Dictionary<uint, CharEnum.SlotDisplay[]>();

        IReadOnlyDictionary<uint, (uint DisplayId, byte InventoryType)> displays;
        try
        {
            displays = await worldDb.GetItemDisplaysAsync(entries, ct);
        }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "CHAR_ENUM paperdoll: БД мира недоступна ({Msg})", ex.Message);
            return new Dictionary<uint, CharEnum.SlotDisplay[]>();
        }

        var result = new Dictionary<uint, CharEnum.SlotDisplay[]>(equippedByChar.Count);
        foreach (var (guid, equipped) in equippedByChar)
        {
            var slots = new CharEnum.SlotDisplay[InventorySlots.EquipmentEnd];
            foreach (var item in equipped)
            {
                if (displays.TryGetValue(item.ItemEntry, out var d))
                    slots[item.Slot] = new CharEnum.SlotDisplay(d.DisplayId, d.InventoryType);
            }

            result[guid] = slots;
        }
        return result;
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgCharCreate)]
    public async Task OnCharCreate(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var name = NormalizeName(reader.CString());
        var race = reader.UInt8();
        var charClass = reader.UInt8();
        var gender = reader.UInt8();
        var skin = reader.UInt8();
        var face = reader.UInt8();
        var hairStyle = reader.UInt8();
        var hairColor = reader.UInt8();
        var facialHair = reader.UInt8();
        // outfitId (uint8) — не используется

        CharResponse result;
        try
        {
            result = await TryCreateAsync(session, name, race, charClass, gender,
                skin, face, hairStyle, hairColor, facialHair, ct);
        }
        catch (Exception ex)
        {
            // Любой сбой (БД, и т.п.) НЕ должен рвать соединение — иначе клиент вылетает.
            session.Logger.LogError(ex, "CHAR_CREATE '{Name}' для '{User}' — ошибка", name, session.Account);
            result = CharResponse.CreateError;
        }

        await session.SendAsync(WorldOpcode.SmsgCharCreate, new ByteWriter(1).UInt8((byte)result).ToArray(), ct);
        session.Logger.LogInformation("CHAR_CREATE '{Name}' для '{User}' → {Result}", name, session.Account, result);
    }

    private async Task<CharResponse> TryCreateAsync(WorldSession session, string name, byte race, byte charClass,
        byte gender, byte skin, byte face, byte hairStyle, byte hairColor, byte facialHair, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return CharResponse.CreateFailed;
        if (name.Length is < 2 or > 12)   // лимит имени WoW — 12 символов (столбец name VARCHAR(12))
            return CharResponse.CreateFailed;
        if (await characters.CountByAccountAsync(session.AccountId, ct) >= ICharacterRepository.MaxCharactersPerAccount)
            return CharResponse.CreateServerLimit;
        if (await characters.NameExistsAsync(name, ct))
            return CharResponse.CreateNameInUse;

        var start = StartPositions.ForRace(race);
        var character = new Character
        {
            AccountId = session.AccountId,
            Name = name,
            Race = race,
            Class = charClass,
            Gender = gender,
            Skin = skin,
            Face = face,
            HairStyle = hairStyle,
            HairColor = hairColor,
            FacialHair = facialHair,
            Level = 1,
            Zone = start.Zone,
            Map = start.Map,
            X = start.X,
            Y = start.Y,
            Z = start.Z,
        };
        var newGuid = await characters.CreateAsync(character, ct);

        // M6.1: выдать стартовую экипировку (шмот/оружие), чтобы персонаж входил в мир не голым.
        await startingGear.GiveAsync(session, newGuid, race, charClass, ct);

        return CharResponse.CreateSuccess;
    }

    /// <summary>
    /// Склонения имени (ruRU-клиент после создания персонажа). Сами склонения пока не храним —
    /// просто подтверждаем приём, иначе клиент зависает на «Обновление персонажа…».
    /// </summary>
    [WorldOpcodeHandler(WorldOpcode.CmsgSetPlayerDeclinedNames)]
    public async Task OnSetDeclinedNames(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var guid = reader.UInt64();
        reader.CString();                       // имя (не нужно)
        var declined = new string[5];
        for (var i = 0; i < 5; i++)
            declined[i] = reader.CString();

        // Сохраняем — иначе ruRU-клиент спрашивает склонения при каждом входе.
        try { await characters.SetDeclinedNamesAsync((uint)guid, declined, ct); }
        catch (Exception ex) { session.Logger.LogWarning(ex, "SET_DECLINED_NAMES guid={Guid}: {Msg}", guid, ex.Message); }

        var w = new ByteWriter(12)
            .UInt32(0)        // result = 0 (успех)
            .UInt64(guid);
        await session.SendAsync(WorldOpcode.SmsgSetPlayerDeclinedNamesResult, w.ToArray(), ct);
        session.Logger.LogInformation("SET_DECLINED_NAMES guid={Guid} сохранены", guid);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgCharDelete)]
    public async Task OnCharDelete(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var guid = (uint)reader.UInt64();
        var deleted = await characters.DeleteAsync(guid, session.AccountId, ct);

        await session.SendAsync(WorldOpcode.SmsgCharDelete,
            new ByteWriter(1).UInt8((byte)(deleted ? CharResponse.DeleteSuccess : CharResponse.DeleteFailed)).ToArray(), ct);
        session.Logger.LogInformation("CHAR_DELETE guid={Guid} для '{User}' → {Ok}", guid, session.Account, deleted);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgNameQuery)]
    public async Task OnNameQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var guid = (uint)reader.UInt64();

        var character = await characters.GetByGuidAsync(guid, ct);
        if (character is null)
            return;

        string[]? declined = null;
        try { declined = await characters.GetDeclinedNamesAsync(guid, ct); }
        catch { /* нет таблицы/данных — отдаём без склонений */ }
        var hasDeclined = declined is not null && declined.Any(s => !string.IsNullOrEmpty(s));

        var w = new ByteWriter(64);
        PackedGuid.Write(w, guid);
        w.UInt8(0)                              // early_terminate = 0
         .CString(character.Name)
         .CString(string.Empty)                 // кросс-реалм имя
         .UInt8(character.Race)
         .UInt8(character.Gender)
         .UInt8(character.Class)
         .UInt8((byte)(hasDeclined ? 1 : 0));   // has_declined_names
        if (hasDeclined)
        {
            for (var i = 0; i < 5; i++)
                w.CString(declined!.ElementAtOrDefault(i) ?? string.Empty);
        }

        await session.SendAsync(WorldOpcode.SmsgNameQueryResponse, w.ToArray(), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgQueryTime)]
    public async Task OnQueryTime(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var w = new ByteWriter(8)
            .UInt32((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            .UInt32(0); // время до ежедневного сброса
        await session.SendAsync(WorldOpcode.SmsgQueryTimeResponse, w.ToArray(), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgRealmSplit)]
    public async Task OnRealmSplit(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var clientState = reader.UInt32();
        var w = new ByteWriter(16)
            .UInt32(clientState)
            .UInt32(0)              // SPLIT_NORMAL
            .CString("01/01/01");
        await session.SendAsync(WorldOpcode.SmsgRealmSplit, w.ToArray(), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgReadyForAccountDataTimes)]
    public Task OnReadyForAccountData(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => SendAccountDataTimesAsync(session, 0x15, ct); // GLOBAL_CACHE_MASK

    /// <summary>GLOBAL_CACHE_MASK (типы 0,2,4 — account-wide); остальные (1,3,5,6,7) — per-character. M7 #17.</summary>
    private const uint GlobalCacheMask = 0x15;

    /// <summary>Глобальный (account-wide) ли тип account-data — иначе per-character. M7 #17.</summary>
    private static bool IsGlobalType(uint dataType) => dataType < 8 && (GlobalCacheMask & (1u << (int)dataType)) != 0;

    /// <summary>owner_id + is_char для типа: глобальный → account_id, per-character → guid персонажа. M7 #17.</summary>
    private static (uint OwnerId, bool IsChar) OwnerFor(WorldSession session, uint dataType)
        => IsGlobalType(dataType) ? (session.AccountId, false) : (session.InWorldGuid, true);

    [WorldOpcodeHandler(WorldOpcode.CmsgUpdateAccountData)]
    public async Task OnUpdateAccountData(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var dataType = reader.UInt32();
        var time = reader.UInt32();
        // Остаток тела = decompressed_size(u32) + zlib-данные. Храним как есть (кросс-девайс хранилище,
        // сервер не распаковывает). M7 #17.
        var blob = packet.Body.Length > 8 ? packet.Body[8..] : [];

        var (ownerId, isChar) = OwnerFor(session, dataType);
        if (ownerId != 0)
        {
            try { await charState.UpsertAccountDataAsync(ownerId, isChar, (byte)dataType, time, blob, ct); }
            catch (Exception ex) { session.Logger.LogDebug(ex, "UPDATE_ACCOUNT_DATA type={Type}: {Msg}", dataType, ex.Message); }
        }

        // Подтверждаем приём (иначе клиент зацикливается при входе).
        await session.SendAsync(WorldOpcode.SmsgUpdateAccountDataComplete,
            new ByteWriter(8).UInt32(dataType).UInt32(0).ToArray(), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgRequestAccountData)]
    public async Task OnRequestAccountData(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var dataType = packet.Reader().UInt32();
        var (ownerId, isChar) = OwnerFor(session, dataType);

        uint time = 0;
        byte[] blob = [];
        if (ownerId != 0)
        {
            try
            {
                var stored = await charState.GetAccountDataAsync(ownerId, isChar, (byte)dataType, ct);
                if (stored is { } s) { time = s.Time; blob = s.Data; }
            }
            catch (Exception ex) { session.Logger.LogDebug(ex, "REQUEST_ACCOUNT_DATA type={Type}: {Msg}", dataType, ex.Message); }
        }

        // SMSG_UPDATE_ACCOUNT_DATA (3.3.5, формат CMaNGOS, MiscHandler.cpp:881):
        //   data << (_player ? _player->GetObjectGuid() : ObjectGuid()); // FULL u64, НЕ packed!
        //   data << uint32(type) << uint32(time) << uint32(decompressed_size) << zlib;
        // Раньше тут был PackedGuid → для guid=0 уходил 1 байт вместо 8, клиент сдвигался на 7
        // байт в потоке, читал мусор как decompressed_size, пытался выделить буфер этого размера
        // и валился в access violation (KB#87). При пустом блобе крах не проявлялся, т.к. клиент
        // при decompressed_size=0 ничего не распаковывал.
        var w = new ByteWriter(20 + blob.Length);
        w.UInt64((ulong)session.InWorldGuid); // 0 вне мира → 8 байт нулей
        w.UInt32(dataType);
        w.UInt32(time);
        if (blob.Length > 0)
            w.Bytes(blob);
        else
            w.UInt32(0); // decompressed_size = 0 (нет данных)
        await session.SendAsync(WorldOpcode.SmsgUpdateAccountData, w.ToArray(), ct);
    }

    /// <summary>
    /// Времена account-data (SMSG_ACCOUNT_DATA_TIMES). Экран персонажей — глобальная маска (0x15),
    /// вход в мир — per-character (0xEA). Шлём реальные времена сохранённых блобов, чтобы клиент знал,
    /// что на сервере есть данные, и запросил их (CMSG_REQUEST_ACCOUNT_DATA). M7 #17.
    /// </summary>
    internal async Task SendAccountDataTimesAsync(WorldSession session, uint mask, CancellationToken ct)
    {
        // Времена: для глобальных типов — по аккаунту, для per-character — по персонажу.
        IReadOnlyDictionary<byte, uint> accountTimes = new Dictionary<byte, uint>();
        IReadOnlyDictionary<byte, uint> charTimes = new Dictionary<byte, uint>();
        try
        {
            if (session.AccountId != 0)
                accountTimes = await charState.GetAccountDataTimesAsync(session.AccountId, false, ct);
            if (session.InWorldGuid != 0)
                charTimes = await charState.GetAccountDataTimesAsync(session.InWorldGuid, true, ct);
        }
        catch (Exception ex) { session.Logger.LogDebug(ex, "ACCOUNT_DATA_TIMES: {Msg}", ex.Message); }

        var w = new ByteWriter(48)
            .UInt32((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            .UInt8(1)
            .UInt32(mask);
        for (var i = 0; i < 8; i++)
        {
            if ((mask & (1u << i)) == 0)
                continue;
            var times = IsGlobalType((uint)i) ? accountTimes : charTimes;
            w.UInt32(times.TryGetValue((byte)i, out var t) ? t : 0u);
        }
        await session.SendAsync(WorldOpcode.SmsgAccountDataTimes, w.ToArray(), ct);
    }

    /// <summary>Имя WoW: первая буква заглавная, остальные строчные.</summary>
    private static string NormalizeName(string name)
    {
        name = name.Trim();
        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();
    }
}
