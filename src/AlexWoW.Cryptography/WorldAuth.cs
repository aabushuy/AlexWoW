using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AlexWoW.Cryptography;

/// <summary>Помощники аутентификации на world-сервере (CMSG_AUTH_SESSION).</summary>
public static class WorldAuth
{
    /// <summary>
    /// Вычисляет digest, которым клиент доказывает знание session key:
    /// SHA1( UPPER(account) || uint32(0) || clientSeed || authSeed || sessionKey ).
    /// Все uint32 — little-endian. Сервер сравнивает результат с digest из пакета.
    /// </summary>
    public static byte[] ComputeAuthSessionDigest(
        string account, uint clientSeed, uint authSeed, byte[] sessionKey)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);

        sha.AppendData(Encoding.ASCII.GetBytes(account.ToUpperInvariant()));

        Span<byte> u32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(u32, 0);
        sha.AppendData(u32);
        BinaryPrimitives.WriteUInt32LittleEndian(u32, clientSeed);
        sha.AppendData(u32);
        BinaryPrimitives.WriteUInt32LittleEndian(u32, authSeed);
        sha.AppendData(u32);

        sha.AppendData(sessionKey);

        return sha.GetHashAndReset();
    }
}
