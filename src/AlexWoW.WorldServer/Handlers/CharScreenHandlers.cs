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
        await session.SendAsync(WorldOpcode.SmsgCharEnum, CharEnum.BuildBody(list), ct);
        session.Logger.LogInformation("CHAR_ENUM: {Count} персонажей для '{User}'", list.Count, session.Account);
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

        var result = await TryCreateAsync(session, name, race, charClass, gender,
            skin, face, hairStyle, hairColor, facialHair, ct);

        await session.SendAsync(WorldOpcode.SmsgCharCreate, new ByteWriter(1).UInt8((byte)result).ToArray(), ct);
        session.Logger.LogInformation("CHAR_CREATE '{Name}' для '{User}' → {Result}", name, session.Account, result);
    }

    private static async Task<CharResponse> TryCreateAsync(WorldSession session, string name, byte race, byte charClass,
        byte gender, byte skin, byte face, byte hairStyle, byte hairColor, byte facialHair, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
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
        await session.Characters.CreateAsync(character, ct);
        return CharResponse.CreateSuccess;
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

        var w = new ByteWriter(48);
        PackedGuid.Write(w, guid);
        w.UInt8(0)                              // имя известно
         .CString(character.Name)
         .CString(string.Empty)                 // кросс-реалм имя
         .UInt8(character.Race)
         .UInt8(character.Gender)
         .UInt8(character.Class)
         .UInt8(0);                             // имя не склоняется
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
