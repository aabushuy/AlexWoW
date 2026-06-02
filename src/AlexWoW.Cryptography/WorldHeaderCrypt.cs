using System.Security.Cryptography;

namespace AlexWoW.Cryptography;

/// <summary>
/// Шифрование заголовков world-пакетов (WotLK 3.3.5a). Инициализируется session key
/// после CMSG_AUTH_SESSION. Ключи RC4 выводятся через HMAC-SHA1 из session key с
/// фиксированными «сидами» (совпадают с TrinityCore — иначе клиент не сойдётся).
///
/// Шифруется ТОЛЬКО заголовок пакета (размер + opcode); тело идёт открытым текстом.
/// </summary>
public sealed class WorldHeaderCrypt
{
    // Фиксированные HMAC-сиды (TrinityCore, WotLK).
    private static readonly byte[] ServerEncryptionKey =
    [
        0xCC, 0x98, 0xAE, 0x04, 0xE8, 0x97, 0xEA, 0xCA, 0x12, 0xDD,
        0xC0, 0x93, 0x42, 0x91, 0x53, 0x57,
    ];

    private static readonly byte[] ServerDecryptionKey =
    [
        0xC2, 0xB3, 0x72, 0x3C, 0xC6, 0xAE, 0xD9, 0xB5, 0x34, 0x3C,
        0x53, 0xEE, 0x2F, 0x43, 0x67, 0xCE,
    ];

    private const int DropBytes = 1024;

    private Arc4? _encrypt; // сервер → клиент
    private Arc4? _decrypt; // клиент → сервер

    public bool IsInitialized => _encrypt is not null;

    /// <summary>Инициализирует шифрование по 40-байтовому session key.</summary>
    public void Init(byte[] sessionKey)
    {
        var encryptKey = HMACSHA1.HashData(ServerEncryptionKey, sessionKey);
        var decryptKey = HMACSHA1.HashData(ServerDecryptionKey, sessionKey);

        _encrypt = new Arc4(encryptKey);
        _decrypt = new Arc4(decryptKey);

        // Прогрев — отбрасываем слабое начало RC4-потока.
        _encrypt.Drop(DropBytes);
        _decrypt.Drop(DropBytes);
    }

    /// <summary>Шифрует исходящий заголовок (сервер → клиент) на месте.</summary>
    public void Encrypt(Span<byte> header)
    {
        if (_encrypt is null)
            throw new InvalidOperationException("WorldHeaderCrypt не инициализирован.");
        _encrypt.Process(header);
    }

    /// <summary>Дешифрует входящий заголовок (клиент → сервер) на месте.</summary>
    public void Decrypt(Span<byte> header)
    {
        if (_decrypt is null)
            throw new InvalidOperationException("WorldHeaderCrypt не инициализирован.");
        _decrypt.Process(header);
    }
}
