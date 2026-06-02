using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using AlexWoW.Common.Network;
using AlexWoW.Cryptography;
using AlexWoW.Database;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Net;

/// <summary>
/// Обслуживает одно world-соединение. Заголовки пакетов после handshake шифруются
/// (RC4), тело — открытым текстом. Формат заголовков (3.3.5a):
///   сервер → клиент: uint16 size (big-endian) + uint16 opcode (LE)
///   клиент → сервер: uint16 size (big-endian) + uint32 opcode (LE)
/// size включает длину opcode.
/// </summary>
public sealed class WorldSession(
    Socket socket,
    AuthDatabase database,
    CharactersDatabase characters,
    WorldServerOptions options,
    ILogger logger)
{
    private const int ServerHeaderSize = 4; // 2 size + 2 opcode
    private const int ClientHeaderSize = 6; // 2 size + 4 opcode

    private readonly NetworkStream _stream = new(socket, ownsSocket: true);
    private readonly string _remoteIp =
        (socket.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "?";
    private readonly WorldHeaderCrypt _crypt = new();

    private uint _authSeed;
    private string? _account;
    private uint _accountId;

    // Текущее состояние игрока в мире (guid != 0, пока в мире).
    private uint _inWorldGuid;
    private float _posX, _posY, _posZ, _posO;

    // Опкоды движения: все несут packed guid + MovementInfo с позицией.
    private static readonly HashSet<WorldOpcode> MovementOpcodes =
    [
        WorldOpcode.MsgMoveStartForward, WorldOpcode.MsgMoveStartBackward, WorldOpcode.MsgMoveStop,
        WorldOpcode.MsgMoveStartStrafeLeft, WorldOpcode.MsgMoveStartStrafeRight, WorldOpcode.MsgMoveStopStrafe,
        WorldOpcode.MsgMoveJump, WorldOpcode.MsgMoveStartTurnLeft, WorldOpcode.MsgMoveStartTurnRight,
        WorldOpcode.MsgMoveStopTurn, WorldOpcode.MsgMoveStartPitchUp, WorldOpcode.MsgMoveStartPitchDown,
        WorldOpcode.MsgMoveStopPitch, WorldOpcode.MsgMoveSetRunMode, WorldOpcode.MsgMoveSetWalkMode,
        WorldOpcode.MsgMoveFallLand, WorldOpcode.MsgMoveStartSwim, WorldOpcode.MsgMoveStopSwim,
        WorldOpcode.MsgMoveSetFacing, WorldOpcode.MsgMoveSetPitch, WorldOpcode.MsgMoveHeartbeat,
    ];

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await SendAuthChallengeAsync(ct);

            while (!ct.IsCancellationRequested)
            {
                var packet = await ReadPacketAsync(ct);
                if (packet is null)
                    break;

                var (opcode, body) = packet.Value;
                await DispatchAsync(opcode, body, ct);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException or EndOfStreamException)
        {
            logger.LogDebug("World-соединение {Ip} закрыто: {Message}", _remoteIp, ex.Message);
        }
        finally
        {
            await SavePositionIfInWorldAsync(CancellationToken.None);
            await _stream.DisposeAsync();
        }
    }

    private async Task DispatchAsync(WorldOpcode opcode, byte[] body, CancellationToken ct)
    {
        switch (opcode)
        {
            case WorldOpcode.CmsgAuthSession:
                await HandleAuthSessionAsync(body, ct);
                break;
            case WorldOpcode.CmsgPing:
                await HandlePingAsync(body, ct);
                break;
            case WorldOpcode.CmsgReadyForAccountDataTimes:
                await SendAccountDataTimesAsync(0x15, ct); // GLOBAL_CACHE_MASK
                break;
            case WorldOpcode.CmsgUpdateAccountData:
                await HandleUpdateAccountDataAsync(body, ct);
                break;
            case WorldOpcode.CmsgRealmSplit:
                await HandleRealmSplitAsync(body, ct);
                break;
            case WorldOpcode.CmsgCharEnum:
                await HandleCharEnumAsync(ct);
                break;
            case WorldOpcode.CmsgCharCreate:
                await HandleCharCreateAsync(body, ct);
                break;
            case WorldOpcode.CmsgCharDelete:
                await HandleCharDeleteAsync(body, ct);
                break;
            case WorldOpcode.CmsgPlayerLogin:
                await HandlePlayerLoginAsync(body, ct);
                break;
            case WorldOpcode.CmsgLogoutRequest:
                await HandleLogoutRequestAsync(ct);
                break;
            case WorldOpcode.CmsgLogoutCancel:
                await SendPacketAsync(WorldOpcode.SmsgLogoutCancelAck, [], ct);
                break;
            case WorldOpcode.CmsgTimeSyncResp:
                logger.LogInformation("CMSG_TIME_SYNC_RESP получен — пост-спавн пакеты доходят, поток в порядке");
                break;
            case WorldOpcode.CmsgNameQuery:
                await HandleNameQueryAsync(body, ct);
                break;
            case WorldOpcode.CmsgQueryTime:
                await SendQueryTimeResponseAsync(ct);
                break;
            case WorldOpcode.CmsgMessageChat:
                await HandleChatAsync(body, ct);
                break;
            default:
                if (MovementOpcodes.Contains(opcode))
                    HandleMovement(body);
                else
                    logger.LogInformation("Опкод {Opcode} (0x{Value:X}) от {Ip} — пока без обработчика",
                        opcode, (uint)opcode, _remoteIp);
                break;
        }
    }

    // --- Handshake -----------------------------------------------------------

    private async Task SendAuthChallengeAsync(CancellationToken ct)
    {
        Span<byte> seedBytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(seedBytes);
        _authSeed = BinaryPrimitives.ReadUInt32LittleEndian(seedBytes);

        var payload = new ByteWriter(40)
            .UInt32(1)
            .UInt32(_authSeed)
            .Bytes(RandomNumberGenerator.GetBytes(32)) // seed1/seed2, клиентом для нашей схемы не используются
            .ToArray();

        await SendPacketAsync(WorldOpcode.SmsgAuthChallenge, payload, ct);
        logger.LogInformation("Отправлен SMSG_AUTH_CHALLENGE на {Ip}", _remoteIp);
    }

    private async Task HandleAuthSessionAsync(byte[] body, CancellationToken ct)
    {
        var reader = new ByteReader(body);
        var build = reader.UInt32();      // build
        reader.UInt32();                  // loginServerId
        _account = reader.CString().ToUpperInvariant();
        reader.UInt32();                  // loginServerType
        var clientSeed = reader.UInt32(); // local challenge
        reader.UInt32();                  // regionId
        reader.UInt32();                  // battlegroupId
        reader.UInt32();                  // realmId
        reader.Skip(8);                   // dosResponse (uint64)
        var clientDigest = reader.Bytes(20).ToArray();

        logger.LogInformation("CMSG_AUTH_SESSION: '{User}' (build {Build}) от {Ip}", _account, build, _remoteIp);

        if (build != options.ExpectedBuild)
        {
            logger.LogWarning("Неподдерживаемый build {Build} (ожидается {Expected}) от {Ip}",
                build, options.ExpectedBuild, _remoteIp);
            return;
        }

        var account = await database.GetAccountByUsernameAsync(_account, ct);
        if (account?.SessionKey is null)
        {
            logger.LogWarning("Нет session key для '{User}' — клиент не проходил логин?", _account);
            return; // закрываем соединение
        }

        var expected = WorldAuth.ComputeAuthSessionDigest(_account, clientSeed, _authSeed, account.SessionKey);
        if (!CryptographicOperations.FixedTimeEquals(expected, clientDigest))
        {
            logger.LogWarning("Неверный auth digest для '{User}' от {Ip}", _account, _remoteIp);
            return;
        }

        _accountId = account.Id;

        // С этого момента заголовки шифруются.
        _crypt.Init(account.SessionKey);

        var response = new ByteWriter(11)
            .UInt8((byte)AuthResponseCode.Ok)
            .UInt32(0)              // billing time remaining
            .UInt8(0)               // billing flags
            .UInt32(0)              // billing time rested
            .UInt8(2)               // expansion: 2 = WotLK
            .ToArray();

        await SendPacketAsync(WorldOpcode.SmsgAuthResponse, response, ct);
        logger.LogInformation("Успешный world-вход '{User}' от {Ip} (шифрование включено)", _account, _remoteIp);
    }

    private async Task HandlePingAsync(byte[] body, CancellationToken ct)
    {
        var reader = new ByteReader(body);
        var ping = reader.UInt32();        // sequence
        var pong = new ByteWriter(4).UInt32(ping).ToArray();
        await SendPacketAsync(WorldOpcode.SmsgPong, pong, ct);
    }

    // --- Экран персонажей (M3) -----------------------------------------------

    private async Task SendAccountDataTimesAsync(uint mask, CancellationToken ct)
    {
        var w = new ByteWriter(48)
            .UInt32((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            .UInt8(1)
            .UInt32(mask);
        for (var i = 0; i < 8; i++)
            if ((mask & (1u << i)) != 0)
                w.UInt32(0);
        await SendPacketAsync(WorldOpcode.SmsgAccountDataTimes, w.ToArray(), ct);
    }

    private async Task HandleUpdateAccountDataAsync(byte[] body, CancellationToken ct)
    {
        // Достаточно подтвердить приём, иначе клиент зацикливается при входе.
        var reader = new ByteReader(body);
        var dataType = reader.UInt32();
        var w = new ByteWriter(8).UInt32(dataType).UInt32(0);
        await SendPacketAsync(WorldOpcode.SmsgUpdateAccountDataComplete, w.ToArray(), ct);
    }

    private async Task HandleRealmSplitAsync(byte[] body, CancellationToken ct)
    {
        var reader = new ByteReader(body);
        var clientState = reader.UInt32();
        var w = new ByteWriter(16)
            .UInt32(clientState)
            .UInt32(0)              // RealmSplitState: 0 = SPLIT_NORMAL
            .CString("01/01/01");   // split date
        await SendPacketAsync(WorldOpcode.SmsgRealmSplit, w.ToArray(), ct);
    }

    private async Task HandleCharEnumAsync(CancellationToken ct)
    {
        var list = await characters.GetByAccountAsync(_accountId, ct);
        await SendPacketAsync(WorldOpcode.SmsgCharEnum, CharEnum.BuildBody(list), ct);
        logger.LogInformation("CHAR_ENUM: {Count} персонажей для '{User}'", list.Count, _account);
    }

    private async Task HandleCharCreateAsync(byte[] body, CancellationToken ct)
    {
        var reader = new ByteReader(body);
        var name = NormalizeName(reader.CString());
        var race = reader.UInt8();
        var charClass = reader.UInt8();
        var gender = reader.UInt8();
        var skin = reader.UInt8();
        var face = reader.UInt8();
        var hairStyle = reader.UInt8();
        var hairColor = reader.UInt8();
        var facialHair = reader.UInt8();
        // reader: outfitId (uint8) — не используется

        var result = await TryCreateCharacterAsync(
            name, race, charClass, gender, skin, face, hairStyle, hairColor, facialHair, ct);

        await SendPacketAsync(WorldOpcode.SmsgCharCreate,
            new ByteWriter(1).UInt8((byte)result).ToArray(), ct);
        logger.LogInformation("CHAR_CREATE '{Name}' для '{User}' → {Result}", name, _account, result);
    }

    private async Task<CharResponse> TryCreateCharacterAsync(
        string name, byte race, byte charClass, byte gender,
        byte skin, byte face, byte hairStyle, byte hairColor, byte facialHair, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return CharResponse.CreateFailed;

        if (await characters.CountByAccountAsync(_accountId, ct) >= CharactersDatabase.MaxCharactersPerAccount)
            return CharResponse.CreateServerLimit;

        if (await characters.NameExistsAsync(name, ct))
            return CharResponse.CreateNameInUse;

        var start = StartPositions.ForRace(race);
        var character = new Character
        {
            AccountId = _accountId,
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

        await characters.CreateAsync(character, ct);
        return CharResponse.CreateSuccess;
    }

    private async Task HandleCharDeleteAsync(byte[] body, CancellationToken ct)
    {
        var reader = new ByteReader(body);
        var guid = (uint)reader.UInt64();
        var deleted = await characters.DeleteAsync(guid, _accountId, ct);

        await SendPacketAsync(WorldOpcode.SmsgCharDelete,
            new ByteWriter(1).UInt8((byte)(deleted ? CharResponse.DeleteSuccess : CharResponse.DeleteFailed)).ToArray(), ct);
        logger.LogInformation("CHAR_DELETE guid={Guid} для '{User}' → {Ok}", guid, _account, deleted);
    }

    // --- Вход в мир (M4) -----------------------------------------------------

    private async Task HandlePlayerLoginAsync(byte[] body, CancellationToken ct)
    {
        var reader = new ByteReader(body);
        var guid = (uint)reader.UInt64();

        var character = await characters.GetByGuidAsync(guid, ct);
        if (character is null || character.AccountId != _accountId)
        {
            logger.LogWarning("PLAYER_LOGIN: персонаж guid={Guid} не найден/чужой для '{User}'", guid, _account);
            return;
        }

        // Запоминаем позицию для серверного трекинга и сохранения при выходе.
        _inWorldGuid = character.Guid;
        _posX = character.X;
        _posY = character.Y;
        _posZ = character.Z;

        // Последовательность входа в мир (порядок как в эталонных ядрах).
        // 1) Подтверждение мира: карта + позиция.
        var verify = new ByteWriter(20)
            .UInt32(character.Map)
            .Single(character.X).Single(character.Y).Single(character.Z)
            .Single(0f);
        await SendPacketAsync(WorldOpcode.SmsgLoginVerifyWorld, verify.ToArray(), ct);

        // 2) Времена кэша аккаунта (per-character mask).
        await SendAccountDataTimesAsync(0xEA, ct);

        // 3) Статус системных фич (complaint + voice).
        await SendPacketAsync(WorldOpcode.SmsgFeatureSystemStatus,
            new ByteWriter(2).UInt8(2).UInt8(0).ToArray(), ct);

        // 3.5) Спеллбук: минимум — языковые спеллы, иначе клиент блокирует /say.
        await SendInitialSpellsAsync(character.Race, ct);

        // 4) Флаги обучения (8 × uint32) — выключаем подсказки.
        var tutorials = new ByteWriter(32);
        for (var i = 0; i < 8; i++)
            tutorials.UInt32(0);
        await SendPacketAsync(WorldOpcode.SmsgTutorialFlags, tutorials.ToArray(), ct);

        // 5) Установка игрового времени.
        await SendLoginTimeSpeedAsync(ct);

        // 6) Спавн собственного игрока (CREATE_OBJECT2).
        var spawn = PlayerSpawn.BuildCreateObject(character, (uint)Environment.TickCount);
        await SendPacketAsync(WorldOpcode.SmsgUpdateObject, spawn, ct);

        // 7) Старт синхронизации времени — без неё игрок не управляется.
        await SendPacketAsync(WorldOpcode.SmsgTimeSyncReq, new ByteWriter(4).UInt32(0).ToArray(), ct);

        logger.LogInformation("PLAYER_LOGIN '{Name}' (guid={Guid}) → мир: map={Map} ({X};{Y};{Z})",
            character.Name, guid, character.Map, character.X, character.Y, character.Z);
    }

    private async Task HandleNameQueryAsync(byte[] body, CancellationToken ct)
    {
        var reader = new ByteReader(body);
        var guid = (uint)reader.UInt64();

        var character = await characters.GetByGuidAsync(guid, ct);
        if (character is null)
            return;

        var w = new ByteWriter(48);
        PackedGuid.Write(w, guid);
        w.UInt8(0)                  // 0 = имя известно
         .CString(character.Name)
         .CString(string.Empty)     // кросс-реалм имя (пусто)
         .UInt8(character.Race)
         .UInt8(character.Gender)
         .UInt8(character.Class)
         .UInt8(0);                 // имя не склоняется
        await SendPacketAsync(WorldOpcode.SmsgNameQueryResponse, w.ToArray(), ct);
    }

    private async Task SendQueryTimeResponseAsync(CancellationToken ct)
    {
        var w = new ByteWriter(8)
            .UInt32((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            .UInt32(0); // время до ежедневного сброса
        await SendPacketAsync(WorldOpcode.SmsgQueryTimeResponse, w.ToArray(), ct);
    }

    private async Task SendInitialSpellsAsync(byte race, CancellationToken ct)
    {
        var spells = LanguageSpells.ForRace(race);
        var w = new ByteWriter(8 + spells.Count * 6)
            .UInt8(0)
            .UInt16((ushort)spells.Count);
        foreach (var spell in spells)
            w.UInt32((uint)spell).UInt16(0); // 3.3.5: spellId — u32 + u16 (on-cooldown)
        w.UInt16(0); // нет кулдаунов
        await SendPacketAsync(WorldOpcode.SmsgInitialSpells, w.ToArray(), ct);
    }

    private async Task HandleChatAsync(byte[] body, CancellationToken ct)
    {
        var reader = new ByteReader(body);
        var type = reader.UInt32();        // тип чата (say/yell/emote…)
        reader.UInt32();                   // язык клиента (расовый) — игнорируем
        var rest = reader.Bytes(reader.Remaining);
        var len = rest.Length;
        while (len > 0 && rest[len - 1] == 0)
            len--;
        if (len == 0)
            return;
        var msg = rest[..len].ToArray();   // сырые байты — без перекодировки (Кириллица ок)

        logger.LogInformation("CHAT '{User}' type={Type}: {Msg}",
            _account, type, System.Text.Encoding.UTF8.GetString(msg));

        // Эхо отправителю, чтобы он видел свой /say (для одного игрока этого достаточно).
        var w = new ByteWriter(40 + msg.Length)
            .UInt8(1)                      // CHAT_MSG_SAY (display enum); CMSG-тип иной — логируем выше
            .UInt32(0)                     // LANG_UNIVERSAL — понятно всем
            .UInt64(_inWorldGuid)          // отправитель
            .UInt32(0)                     // chat flags
            .UInt64(0)                     // target
            .UInt32((uint)(msg.Length + 1))
            .Bytes(msg).UInt8(0)           // сообщение + null
            .UInt8(0);                     // chat tag
        await SendPacketAsync(WorldOpcode.SmsgMessageChat, w.ToArray(), ct);
    }

    /// <summary>Извлекает позицию из MovementInfo любого MSG_MOVE_* и запоминает её.</summary>
    private void HandleMovement(byte[] body)
    {
        try
        {
            var reader = new ByteReader(body);
            reader.PackedGuid();   // mover guid
            reader.UInt32();       // movement flags
            reader.UInt16();       // movement flags 2
            reader.UInt32();       // time
            _posX = reader.Single();
            _posY = reader.Single();
            _posZ = reader.Single();
            _posO = reader.Single();
        }
        catch (InvalidOperationException)
        {
            // Нестандартный вариант пакета — игнорируем для трекинга позиции.
        }
    }

    private async Task SavePositionIfInWorldAsync(CancellationToken ct)
    {
        if (_inWorldGuid == 0)
            return;
        try
        {
            await characters.SavePositionAsync(_inWorldGuid, _posX, _posY, _posZ, ct);
            logger.LogInformation("Позиция '{User}' сохранена: ({X};{Y};{Z})", _account, _posX, _posY, _posZ);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Не удалось сохранить позицию '{User}': {Msg}", _account, ex.Message);
        }
    }

    private async Task HandleLogoutRequestAsync(CancellationToken ct)
    {
        await SavePositionIfInWorldAsync(ct);
        _inWorldGuid = 0; // персонаж покидает мир
        // reason = 0 (можно выходить), instant = 1 (мгновенный логаут).
        var response = new ByteWriter(5).UInt32(0).UInt8(1);
        await SendPacketAsync(WorldOpcode.SmsgLogoutResponse, response.ToArray(), ct);

        // Завершаем логаут — клиент вернётся к экрану выбора персонажа.
        await SendPacketAsync(WorldOpcode.SmsgLogoutComplete, [], ct);
        logger.LogInformation("LOGOUT '{User}' → возврат к выбору персонажа", _account);
    }

    private async Task SendLoginTimeSpeedAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // Упакованное календарное время (bitfield, как ждёт клиент).
        var packed = (uint)(
            now.Minute
            | (now.Hour << 6)
            | ((int)now.DayOfWeek << 11)
            | ((now.Day - 1) << 14)
            | ((now.Month - 1) << 20)
            | ((now.Year - 2000) << 24));

        var w = new ByteWriter(12)
            .UInt32(packed)
            .Single(0.01666667f)   // скорость игрового времени
            .UInt32(0);            // unk (3.3.5)
        await SendPacketAsync(WorldOpcode.SmsgLoginSetTimeSpeed, w.ToArray(), ct);
    }

    /// <summary>Имя WoW: первая буква заглавная, остальные строчные.</summary>
    private static string NormalizeName(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            return name;
        return char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();
    }

    // --- Низкоуровневый ввод/вывод -------------------------------------------

    private async Task SendPacketAsync(WorldOpcode opcode, byte[] body, CancellationToken ct)
    {
        var header = new byte[ServerHeaderSize];
        var size = (ushort)(body.Length + 2); // +2 байта opcode
        header[0] = (byte)(size >> 8);         // size big-endian
        header[1] = (byte)(size & 0xFF);
        header[2] = (byte)((uint)opcode & 0xFF);        // opcode little-endian (2 байта)
        header[3] = (byte)(((uint)opcode >> 8) & 0xFF);

        if (_crypt.IsInitialized)
            _crypt.Encrypt(header);

        await _stream.WriteAsync(header, ct);
        if (body.Length > 0)
            await _stream.WriteAsync(body, ct);
        await _stream.FlushAsync(ct);
    }

    private async Task<(WorldOpcode Opcode, byte[] Body)?> ReadPacketAsync(CancellationToken ct)
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

        return (opcode, body);
    }
}
