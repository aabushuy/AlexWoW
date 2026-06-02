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
                await SendAccountDataTimesAsync(ct);
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
            default:
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

    private async Task SendAccountDataTimesAsync(CancellationToken ct)
    {
        const uint mask = 0x15; // GLOBAL_CACHE_MASK
        var w = new ByteWriter(48)
            .UInt32((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            .UInt8(1)
            .UInt32(mask);
        for (var i = 0; i < 8; i++)
            if ((mask & (1u << i)) != 0)
                w.UInt32(0);
        await SendPacketAsync(WorldOpcode.SmsgAccountDataTimes, w.ToArray(), ct);
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

        // 1) Подтверждение мира: карта + позиция.
        var verify = new ByteWriter(20)
            .UInt32(character.Map)
            .Single(character.X).Single(character.Y).Single(character.Z)
            .Single(0f);
        await SendPacketAsync(WorldOpcode.SmsgLoginVerifyWorld, verify.ToArray(), ct);

        // 2) Флаги обучения (8 × uint32) — выключаем подсказки.
        var tutorials = new ByteWriter(32);
        for (var i = 0; i < 8; i++)
            tutorials.UInt32(0);
        await SendPacketAsync(WorldOpcode.SmsgTutorialFlags, tutorials.ToArray(), ct);

        // 3) Спавн собственного игрока (CREATE_OBJECT2).
        var spawn = PlayerSpawn.BuildCreateObject(character, (uint)Environment.TickCount);
        await SendPacketAsync(WorldOpcode.SmsgUpdateObject, spawn, ct);

        logger.LogInformation("PLAYER_LOGIN '{Name}' (guid={Guid}) → мир: map={Map} ({X};{Y};{Z})",
            character.Name, guid, character.Map, character.X, character.Y, character.Z);
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
