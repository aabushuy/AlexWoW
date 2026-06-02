using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace AlexWoW.Cryptography;

/// <summary>
/// Общие параметры и помощники протокола SRP6, используемого логин-сервером WoW (build 12340).
/// Алгоритм и константы совпадают с CMaNGOS / TrinityCore, иначе клиент не пройдёт аутентификацию.
/// Все большие числа передаются и хэшируются в формате little-endian.
/// </summary>
public static class Srp6
{
    /// <summary>Размер хэша SHA-1 в байтах.</summary>
    public const int Sha1Length = 20;

    /// <summary>Длина соли (salt) в байтах.</summary>
    public const int SaltLength = 32;

    /// <summary>Длина чисел N, B, A, v в байтах.</summary>
    public const int KeyLength = 32;

    /// <summary>Длина итогового session key.</summary>
    public const int SessionKeyLength = 40;

    /// <summary>Большое простое число N (safe prime), задаётся в big-endian hex как в mangos.</summary>
    public static readonly BigInteger N = ParseHexBigEndian(
        "894B645E89E1535BBDAD5B8B290650530801B18EBFBF5E8FAB3C82872A3E9BB7");

    /// <summary>Генератор g.</summary>
    public static readonly BigInteger G = 7;

    /// <summary>Множитель k для legacy SRP-6 (не 6a). В WoW k = 3.</summary>
    public static readonly BigInteger K = 3;

    /// <summary>N в виде 32-байтового little-endian массива (для передачи клиенту и хэширования).</summary>
    public static readonly byte[] NBytes = ToFixedLittleEndian(N, KeyLength);

    /// <summary>g в виде одного байта.</summary>
    public static readonly byte[] GBytes = [(byte)7];

    private static BigInteger ParseHexBigEndian(string hex)
    {
        // Ведущий "0" гарантирует положительный знак при разборе.
        return BigInteger.Parse("0" + hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    /// <summary>Преобразует big-endian hex в little-endian массив фиксированной длины.</summary>
    public static byte[] HexToLittleEndian(string hex, int length)
        => ToFixedLittleEndian(ParseHexBigEndian(hex), length);

    /// <summary>Беззнаковое little-endian число → BigInteger.</summary>
    public static BigInteger FromLittleEndian(ReadOnlySpan<byte> littleEndian)
        => new(littleEndian, isUnsigned: true, isBigEndian: false);

    /// <summary>BigInteger → little-endian массив ровно <paramref name="length"/> байт (с дополнением нулями).</summary>
    public static byte[] ToFixedLittleEndian(BigInteger value, int length)
    {
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        if (raw.Length == length)
            return raw;

        var result = new byte[length];
        // Копируем младшие байты; если raw длиннее (из-за знакового байта) — лишнее отбрасываем.
        var copy = Math.Min(raw.Length, length);
        Array.Copy(raw, result, copy);
        return result;
    }

    /// <summary>SHA-1 от конкатенации переданных кусков.</summary>
    public static byte[] Sha1(params ReadOnlyMemory<byte>[] parts)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        foreach (var part in parts)
            sha.AppendData(part.Span);
        return sha.GetHashAndReset();
    }

    /// <summary>Хэш имени пользователя: SHA1(UPPER(username)) в ASCII.</summary>
    public static byte[] HashAccountName(string username)
        => SHA1.HashData(Encoding.UTF8.GetBytes(username.ToUpperInvariant()));

    /// <summary>
    /// Вычисляет приватный ключ x = SHA1(salt || SHA1(UPPER(USER):UPPER(PASS))).
    /// Результат интерпретируется как little-endian число.
    /// </summary>
    public static BigInteger CalculateX(string username, string password, byte[] salt)
    {
        var identity = $"{username.ToUpperInvariant()}:{password.ToUpperInvariant()}";
        var inner = SHA1.HashData(Encoding.UTF8.GetBytes(identity));
        var xBytes = Sha1(salt, inner);
        return FromLittleEndian(xBytes);
    }

    /// <summary>Вычисляет верификатор v = g^x mod N.</summary>
    public static BigInteger CalculateVerifier(string username, string password, byte[] salt)
    {
        var x = CalculateX(username, password, salt);
        return BigInteger.ModPow(G, x, N);
    }

    /// <summary>
    /// Выводит 40-байтовый session key из общего секрета S по алгоритму H-Interleave.
    /// Реализация байт-в-байт совпадает с клиентом WoW (TrinityCore SHA1Interleave).
    /// </summary>
    public static byte[] Sha1Interleave(BigInteger s)
    {
        var bytes = ToFixedLittleEndian(s, KeyLength); // 32 байта little-endian

        Span<byte> even = stackalloc byte[KeyLength / 2];
        Span<byte> odd = stackalloc byte[KeyLength / 2];
        for (var i = 0; i < KeyLength / 2; i++)
        {
            even[i] = bytes[2 * i];
            odd[i] = bytes[2 * i + 1];
        }

        // Отбрасываем старшие нулевые байты, ориентируясь на нечётные позиции — как в клиенте.
        var p = KeyLength / 2;
        while (p > 0 && bytes[2 * p - 1] == 0)
            p--;

        var hashEven = SHA1.HashData(even[..p]);
        var hashOdd = SHA1.HashData(odd[..p]);

        var key = new byte[SessionKeyLength];
        for (var i = 0; i < Sha1Length; i++)
        {
            key[2 * i] = hashEven[i];
            key[2 * i + 1] = hashOdd[i];
        }

        return key;
    }
}
