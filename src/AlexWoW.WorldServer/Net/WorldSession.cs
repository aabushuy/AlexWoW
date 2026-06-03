using System.Buffers.Binary;
using System.Net.Sockets;
using AlexWoW.Cryptography;
using AlexWoW.Database;
using AlexWoW.WorldServer.Handlers;
using AlexWoW.WorldServer.Protocol;
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

    public WorldSession(Socket socket, AuthDatabase database, CharactersDatabase characters,
        WorldServerOptions options, ILogger logger)
    {
        _stream = new NetworkStream(socket, ownsSocket: true);
        RemoteIp = (socket.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "?";
        Database = database;
        Characters = characters;
        Options = options;
        Logger = logger;
    }

    // --- Контекст для обработчиков ---
    internal AuthDatabase Database { get; }
    internal CharactersDatabase Characters { get; }
    internal WorldServerOptions Options { get; }
    internal ILogger Logger { get; }
    internal string RemoteIp { get; }

    // --- Состояние сессии ---
    internal uint AuthSeed { get; set; }
    internal string? Account { get; set; }
    internal uint AccountId { get; set; }
    internal uint InWorldGuid { get; set; } // != 0, пока персонаж в мире
    internal float PosX { get; set; }
    internal float PosY { get; set; }
    internal float PosZ { get; set; }
    internal float PosO { get; set; }

    /// <summary>Существа (NPC), показанные клиенту этой сессии (guid → спавн). M5.</summary>
    internal Dictionary<ulong, NpcSpawn> VisibleNpcs { get; } = new();

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
            await _stream.DisposeAsync();
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

        if (_crypt.IsInitialized)
            _crypt.Encrypt(header);

        await _stream.WriteAsync(header, ct);
        if (body.Length > 0)
            await _stream.WriteAsync(body, ct);
        await _stream.FlushAsync(ct);
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
