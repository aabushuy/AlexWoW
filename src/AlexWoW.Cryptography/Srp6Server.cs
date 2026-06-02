using System.Numerics;
using System.Security.Cryptography;

namespace AlexWoW.Cryptography;

/// <summary>
/// Серверная сторона SRP6-аутентификации для одного логин-сеанса.
/// Создаётся при получении CMD_AUTH_LOGON_CHALLENGE, хранит эфемерное состояние (b, B)
/// до прихода CMD_AUTH_LOGON_PROOF.
/// </summary>
public sealed class Srp6Server
{
    private readonly string _username;
    private readonly byte[] _salt;       // 32 байта little-endian
    private readonly BigInteger _verifier;
    private readonly BigInteger _b;      // приватное эфемерное
    private readonly BigInteger _bigB;   // публичное эфемерное B

    public Srp6Server(string username, byte[] salt, byte[] verifier)
    {
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(verifier);
        if (salt.Length != Srp6.SaltLength)
            throw new ArgumentException($"Соль должна быть {Srp6.SaltLength} байт.", nameof(salt));

        _username = username;
        _salt = salt;
        _verifier = Srp6.FromLittleEndian(verifier);

        // b — приватное эфемерное число (256 бит случайных).
        Span<byte> randomB = stackalloc byte[Srp6.KeyLength];
        RandomNumberGenerator.Fill(randomB);
        _b = Srp6.FromLittleEndian(randomB) % Srp6.N;

        // B = (k*v + g^b) mod N
        var gMod = BigInteger.ModPow(Srp6.G, _b, Srp6.N);
        _bigB = ((Srp6.K * _verifier) + gMod) % Srp6.N;
    }

    /// <summary>Публичное эфемерное B — отправляется клиенту (32 байта little-endian).</summary>
    public byte[] B => Srp6.ToFixedLittleEndian(_bigB, Srp6.KeyLength);

    /// <summary>Соль аккаунта (32 байта little-endian).</summary>
    public byte[] Salt => _salt;

    /// <summary>
    /// Проверяет доказательство клиента (M1). При успехе возвращает session key и серверное
    /// доказательство M2 для отправки обратно клиенту.
    /// </summary>
    public bool TryVerifyProof(
        ReadOnlySpan<byte> clientA,
        ReadOnlySpan<byte> clientM1,
        out byte[] sessionKey,
        out byte[] serverM2)
    {
        sessionKey = [];
        serverM2 = [];

        var a = Srp6.FromLittleEndian(clientA);

        // Защита: A mod N не должно быть нулём.
        if (a % Srp6.N == BigInteger.Zero)
            return false;

        var aBytes = Srp6.ToFixedLittleEndian(a, Srp6.KeyLength);
        var bBytes = B;

        // u = SHA1(A || B)
        var u = Srp6.FromLittleEndian(Srp6.Sha1(aBytes, bBytes));

        // S = (A * v^u) ^ b mod N
        var s = BigInteger.ModPow(a * BigInteger.ModPow(_verifier, u, Srp6.N) % Srp6.N, _b, Srp6.N);

        // K = H-Interleave(S)
        var key = Srp6.Sha1Interleave(s);

        // M1 = SHA1( (H(N) xor H(g)) || H(I) || s || A || B || K )
        var hashN = SHA1.HashData(Srp6.NBytes);
        var hashG = SHA1.HashData(Srp6.GBytes);
        var xorNg = new byte[Srp6.Sha1Length];
        for (var i = 0; i < Srp6.Sha1Length; i++)
            xorNg[i] = (byte)(hashN[i] ^ hashG[i]);

        var hashUser = Srp6.HashAccountName(_username);

        var expectedM1 = Srp6.Sha1(xorNg, hashUser, _salt, aBytes, bBytes, key);

        if (!CryptographicOperations.FixedTimeEquals(expectedM1, clientM1))
            return false;

        // M2 = SHA1(A || M1 || K)
        serverM2 = Srp6.Sha1(aBytes, expectedM1, key);
        sessionKey = key;
        return true;
    }
}
