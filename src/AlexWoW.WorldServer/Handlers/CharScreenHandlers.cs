using AlexWoW.Common.Network;
using AlexWoW.Database;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>Экран персонажей (M3): enum/create/delete, запросы имени/времени, realm split, account data.</summary>
public static class CharScreenHandlers
{
    [WorldOpcodeHandler(WorldOpcode.CmsgCharEnum)]
    public static async Task OnCharEnum(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var list = await session.Characters.GetByAccountAsync(session.AccountId, ct);
        var equipment = await BuildPaperdollAsync(session, list, ct);

        // M7 #16: персонажи с заданными склонениями → флаг CHARACTER_FLAG_DECLINED в enum,
        // иначе ruRU-клиент показывает диалог склонений при каждом заходе на экран выбора.
        IReadOnlySet<uint> declined;
        try { declined = await session.Characters.GetGuidsWithDeclinedNamesAsync(list.Select(c => c.Guid).ToList(), ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug("CHAR_ENUM declined-флаги: {Msg}", ex.Message);
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
    private static async Task<IReadOnlyDictionary<uint, CharEnum.SlotDisplay[]>> BuildPaperdollAsync(
        WorldSession session, IReadOnlyList<Character> characters, CancellationToken ct)
    {
        var equippedByChar = new Dictionary<uint, List<InventoryItem>>();
        var entries = new HashSet<uint>();
        foreach (var c in characters)
        {
            var items = await session.Characters.GetItemsAsync(c.Guid, ct);
            var equipped = items.Where(i => i.Bag == InventorySlots.MainBag
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
            displays = await session.WorldDb.GetItemDisplaysAsync(entries, ct);
        }
        catch (Exception ex)
        {
            session.Logger.LogDebug("CHAR_ENUM paperdoll: БД мира недоступна ({Msg})", ex.Message);
            return new Dictionary<uint, CharEnum.SlotDisplay[]>();
        }

        var result = new Dictionary<uint, CharEnum.SlotDisplay[]>(equippedByChar.Count);
        foreach (var (guid, equipped) in equippedByChar)
        {
            var slots = new CharEnum.SlotDisplay[InventorySlots.EquipmentEnd];
            foreach (var item in equipped)
                if (displays.TryGetValue(item.ItemEntry, out var d))
                    slots[item.Slot] = new CharEnum.SlotDisplay(d.DisplayId, d.InventoryType);
            result[guid] = slots;
        }
        return result;
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgCharCreate)]
    public static async Task OnCharCreate(WorldSession session, IncomingPacket packet, CancellationToken ct)
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

    private static async Task<CharResponse> TryCreateAsync(WorldSession session, string name, byte race, byte charClass,
        byte gender, byte skin, byte face, byte hairStyle, byte hairColor, byte facialHair, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return CharResponse.CreateFailed;
        if (name.Length is < 2 or > 12)   // лимит имени WoW — 12 символов (столбец name VARCHAR(12))
            return CharResponse.CreateFailed;
        if (await session.Characters.CountByAccountAsync(session.AccountId, ct) >= CharactersDatabase.MaxCharactersPerAccount)
            return CharResponse.CreateServerLimit;
        if (await session.Characters.NameExistsAsync(name, ct))
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
        var newGuid = await session.Characters.CreateAsync(character, ct);

        // M6.1: выдать стартовую экипировку (шмот/оружие), чтобы персонаж входил в мир не голым.
        await StartingGear.GiveAsync(session, newGuid, race, charClass, ct);

        return CharResponse.CreateSuccess;
    }

    /// <summary>
    /// Склонения имени (ruRU-клиент после создания персонажа). Сами склонения пока не храним —
    /// просто подтверждаем приём, иначе клиент зависает на «Обновление персонажа…».
    /// </summary>
    [WorldOpcodeHandler(WorldOpcode.CmsgSetPlayerDeclinedNames)]
    public static async Task OnSetDeclinedNames(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var guid = reader.UInt64();
        reader.CString();                       // имя (не нужно)
        var declined = new string[5];
        for (var i = 0; i < 5; i++)
            declined[i] = reader.CString();

        // Сохраняем — иначе ruRU-клиент спрашивает склонения при каждом входе.
        try { await session.Characters.SetDeclinedNamesAsync((uint)guid, declined, ct); }
        catch (Exception ex) { session.Logger.LogWarning("SET_DECLINED_NAMES guid={Guid}: {Msg}", guid, ex.Message); }

        var w = new ByteWriter(12)
            .UInt32(0)        // result = 0 (успех)
            .UInt64(guid);
        await session.SendAsync(WorldOpcode.SmsgSetPlayerDeclinedNamesResult, w.ToArray(), ct);
        session.Logger.LogInformation("SET_DECLINED_NAMES guid={Guid} сохранены", guid);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgCharDelete)]
    public static async Task OnCharDelete(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var guid = (uint)reader.UInt64();
        var deleted = await session.Characters.DeleteAsync(guid, session.AccountId, ct);

        await session.SendAsync(WorldOpcode.SmsgCharDelete,
            new ByteWriter(1).UInt8((byte)(deleted ? CharResponse.DeleteSuccess : CharResponse.DeleteFailed)).ToArray(), ct);
        session.Logger.LogInformation("CHAR_DELETE guid={Guid} для '{User}' → {Ok}", guid, session.Account, deleted);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgNameQuery)]
    public static async Task OnNameQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var guid = (uint)reader.UInt64();

        var character = await session.Characters.GetByGuidAsync(guid, ct);
        if (character is null)
            return;

        string[]? declined = null;
        try { declined = await session.Characters.GetDeclinedNamesAsync(guid, ct); }
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
            for (var i = 0; i < 5; i++)
                w.CString(declined!.ElementAtOrDefault(i) ?? string.Empty);
        await session.SendAsync(WorldOpcode.SmsgNameQueryResponse, w.ToArray(), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgQueryTime)]
    public static async Task OnQueryTime(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var w = new ByteWriter(8)
            .UInt32((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            .UInt32(0); // время до ежедневного сброса
        await session.SendAsync(WorldOpcode.SmsgQueryTimeResponse, w.ToArray(), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgRealmSplit)]
    public static async Task OnRealmSplit(WorldSession session, IncomingPacket packet, CancellationToken ct)
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
    public static Task OnReadyForAccountData(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => SendAccountDataTimesAsync(session, 0x15, ct); // GLOBAL_CACHE_MASK

    [WorldOpcodeHandler(WorldOpcode.CmsgUpdateAccountData)]
    public static async Task OnUpdateAccountData(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        // Подтверждаем приём, иначе клиент зацикливается при входе.
        var reader = packet.Reader();
        var dataType = reader.UInt32();
        await session.SendAsync(WorldOpcode.SmsgUpdateAccountDataComplete,
            new ByteWriter(8).UInt32(dataType).UInt32(0).ToArray(), ct);
    }

    /// <summary>Времена кэша аккаунта. Используется на экране персонажей (0x15) и при входе (0xEA).</summary>
    internal static async Task SendAccountDataTimesAsync(WorldSession session, uint mask, CancellationToken ct)
    {
        var w = new ByteWriter(48)
            .UInt32((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            .UInt8(1)
            .UInt32(mask);
        for (var i = 0; i < 8; i++)
            if ((mask & (1u << i)) != 0)
                w.UInt32(0);
        await session.SendAsync(WorldOpcode.SmsgAccountDataTimes, w.ToArray(), ct);
    }

    /// <summary>Имя WoW: первая буква заглавная, остальные строчные.</summary>
    private static string NormalizeName(string name)
    {
        name = name.Trim();
        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();
    }
}
