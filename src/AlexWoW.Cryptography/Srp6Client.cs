using System.Numerics;
using System.Security.Cryptography;

namespace AlexWoW.Cryptography;

/// <summary>
/// Клиентская сторона SRP6. В продакшене её роль играет Wow.exe; здесь она нужна для
/// round-trip тестов и проверки совместимости серверной реализации.
/// </summary>
public sealed class Srp6Client
{
    private readonly string _username;
    private readonly string _password;
    private readonly BigInteger _a;
    private readonly BigInteger _bigA;

    public Srp6Client(string username, string password)
    {
        _username = username;
        _password = password;

        Span<byte> randomA = stackalloc byte[Srp6.KeyLength];
        RandomNumberGenerator.Fill(randomA);
        _a = Srp6.FromLittleEndian(randomA) % Srp6.N;
        _bigA = BigInteger.ModPow(Srp6.G, _a, Srp6.N);
    }

    /// <summary>Публичное эфемерное A (32 байта little-endian).</summary>
    public byte[] A => Srp6.ToFixedLittleEndian(_bigA, Srp6.KeyLength);

    /// <summary>
    /// По challenge от сервера (B и salt) вычисляет доказательство M1 и session key.
    /// </summary>
    public (byte[] M1, byte[] SessionKey) ComputeProof(byte[] serverB, byte[] salt)
    {
        var b = Srp6.FromLittleEndian(serverB);
        var aBytes = A;

        var u = Srp6.FromLittleEndian(Srp6.Sha1(aBytes, serverB));
        var x = Srp6.CalculateX(_username, _password, salt);

        // S = (B - k * g^x) ^ (a + u*x) mod N
        var gx = BigInteger.ModPow(Srp6.G, x, Srp6.N);
        var baseValue = (b - (Srp6.K * gx) % Srp6.N + Srp6.N * Srp6.K) % Srp6.N;
        var exponent = _a + u * x;
        var s = BigInteger.ModPow(baseValue, exponent, Srp6.N);

        var key = Srp6.Sha1Interleave(s);

        var hashN = SHA1.HashData(Srp6.NBytes);
        var hashG = SHA1.HashData(Srp6.GBytes);
        var xorNg = new byte[Srp6.Sha1Length];
        for (var i = 0; i < Srp6.Sha1Length; i++)
            xorNg[i] = (byte)(hashN[i] ^ hashG[i]);

        var hashUser = Srp6.HashAccountName(_username);
        var m1 = Srp6.Sha1(xorNg, hashUser, salt, aBytes, serverB, key);

        return (m1, key);
    }

    /// <summary>Проверяет серверное доказательство M2 = SHA1(A || M1 || K).</summary>
    public bool VerifyServerProof(byte[] m1, byte[] sessionKey, byte[] serverM2)
    {
        var expected = Srp6.Sha1(A, m1, sessionKey);
        return CryptographicOperations.FixedTimeEquals(expected, serverM2);
    }
}
