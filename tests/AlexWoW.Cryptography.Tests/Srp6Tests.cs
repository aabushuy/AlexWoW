using System.Security.Cryptography;
using AlexWoW.Cryptography;

namespace AlexWoW.Cryptography.Tests;

public class Srp6Tests
{
    private static byte[] NewSalt()
    {
        var salt = new byte[Srp6.SaltLength];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    [Fact]
    public void Verifier_IsDeterministic_ForSameSalt()
    {
        var salt = NewSalt();

        var v1 = Srp6.CalculateVerifier("ALEX", "secret", salt);
        var v2 = Srp6.CalculateVerifier("ALEX", "secret", salt);

        Assert.Equal(v1, v2);
    }

    [Fact]
    public void Verifier_IsCaseInsensitive_ForCredentials()
    {
        var salt = NewSalt();

        // Клиент WoW приводит логин и пароль к верхнему регистру.
        var lower = Srp6.CalculateVerifier("alex", "secret", salt);
        var upper = Srp6.CalculateVerifier("ALEX", "SECRET", salt);

        Assert.Equal(lower, upper);
    }

    [Fact]
    public void SessionKey_IsAlways40Bytes()
    {
        var salt = NewSalt();
        var verifier = Srp6.ToFixedLittleEndian(
            Srp6.CalculateVerifier("ALEX", "secret", salt), Srp6.KeyLength);

        var server = new Srp6Server("ALEX", salt, verifier);
        var client = new Srp6Client("ALEX", "secret");

        var (m1, _) = client.ComputeProof(server.B, salt);
        Assert.True(server.TryVerifyProof(client.A, m1, out var sessionKey, out _));
        Assert.Equal(Srp6.SessionKeyLength, sessionKey.Length);
    }

    [Fact]
    public void FullHandshake_WithCorrectPassword_Succeeds_AndKeysMatch()
    {
        const string User = "ALEX";
        const string Password = "hunter2";
        var salt = NewSalt();
        var verifier = Srp6.ToFixedLittleEndian(
            Srp6.CalculateVerifier(User, Password, salt), Srp6.KeyLength);

        var server = new Srp6Server(User, salt, verifier);
        var client = new Srp6Client(User, Password);

        // Клиент вычисляет доказательство по challenge сервера.
        var (clientM1, clientKey) = client.ComputeProof(server.B, salt);

        // Сервер проверяет M1 и формирует M2.
        var ok = server.TryVerifyProof(client.A, clientM1, out var serverKey, out var serverM2);

        Assert.True(ok);
        // Обе стороны независимо вывели одинаковый session key.
        Assert.Equal(clientKey, serverKey);
        // Клиент подтверждает серверное доказательство.
        Assert.True(client.VerifyServerProof(clientM1, clientKey, serverM2));
    }

    [Fact]
    public void FullHandshake_WithWrongPassword_Fails()
    {
        const string User = "ALEX";
        var salt = NewSalt();
        var verifier = Srp6.ToFixedLittleEndian(
            Srp6.CalculateVerifier(User, "correct-Password", salt), Srp6.KeyLength);

        var server = new Srp6Server(User, salt, verifier);
        var attacker = new Srp6Client(User, "wrong-Password");

        var (m1, _) = attacker.ComputeProof(server.B, salt);

        Assert.False(server.TryVerifyProof(attacker.A, m1, out _, out _));
    }

    [Fact]
    public void Constants_HaveExpectedSizes()
    {
        Assert.Equal(Srp6.KeyLength, Srp6.NBytes.Length);
        Assert.Single(Srp6.GBytes);
        Assert.Equal(7, Srp6.GBytes[0]);
    }
}
