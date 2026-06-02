using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using AlexWoW.Common.Network;
using AlexWoW.Cryptography;
using AlexWoW.Database;
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
