using System.Security.Cryptography;
using System.Text;
using AlexWoW.Cryptography;

namespace AlexWoW.Cryptography.Tests;

public class M2CryptoTests
{
    [Fact]
    public void Arc4_MatchesKnownTestVector()
    {
        // Канонический тест-вектор RC4: key="Key", plaintext="Plaintext".
        var cipher = new Arc4("Key"u8);
        var data = Encoding.ASCII.GetBytes("Plaintext");
        cipher.Process(data);

        Assert.Equal(Convert.FromHexString("BBF316E8D940AF0AD3"), data);
    }

    [Fact]
    public void Arc4_IsReversible_WithMatchingKey()
    {
        var key = RandomNumberGenerator.GetBytes(16);
        var original = RandomNumberGenerator.GetBytes(64);

        var data = (byte[])original.Clone();
        new Arc4(key).Process(data);
        Assert.NotEqual(original, data);          // зашифровано

        new Arc4(key).Process(data);              // тот же ключ, свежий поток
        Assert.Equal(original, data);             // восстановлено
    }

    [Fact]
    public void WorldHeaderCrypt_EncryptedHeader_RoundTrips()
    {
        var sessionKey = RandomNumberGenerator.GetBytes(Srp6.SessionKeyLength);

        var server = new WorldHeaderCrypt();
        server.Init(sessionKey);

        // «Зеркало» — вторая сторона с тем же session key (как клиент).
        var peer = new WorldHeaderCrypt();
        peer.Init(sessionKey);

        var header = RandomNumberGenerator.GetBytes(4); // server header = 2 size + 2 opcode
        var wire = (byte[])header.Clone();
        server.Encrypt(wire);
        Assert.NotEqual(header, wire);

        // Поток шифрования детерминирован от session key → второй экземпляр воспроизводит keystream.
        peer.Encrypt(wire);
        Assert.Equal(header, wire);
    }

    [Fact]
    public void WorldHeaderCrypt_ThrowsBeforeInit()
    {
        var crypt = new WorldHeaderCrypt();
        Assert.False(crypt.IsInitialized);
        Assert.Throws<InvalidOperationException>(() => crypt.Encrypt(new byte[4]));
    }

    [Fact]
    public void AuthSessionDigest_Matches_ForSameInputs()
    {
        var sessionKey = RandomNumberGenerator.GetBytes(Srp6.SessionKeyLength);
        const uint clientSeed = 0xDEADBEEF;
        const uint authSeed = 0x12345678;

        var server = WorldAuth.ComputeAuthSessionDigest("ALEX", clientSeed, authSeed, sessionKey);
        var client = WorldAuth.ComputeAuthSessionDigest("alex", clientSeed, authSeed, sessionKey);

        Assert.Equal(server, client); // регистр логина не важен
    }

    [Fact]
    public void AuthSessionDigest_Differs_ForWrongSessionKey()
    {
        var realKey = RandomNumberGenerator.GetBytes(Srp6.SessionKeyLength);
        var wrongKey = RandomNumberGenerator.GetBytes(Srp6.SessionKeyLength);

        var a = WorldAuth.ComputeAuthSessionDigest("ALEX", 1, 2, realKey);
        var b = WorldAuth.ComputeAuthSessionDigest("ALEX", 1, 2, wrongKey);

        Assert.NotEqual(a, b);
    }
}
