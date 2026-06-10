using System.Security.Cryptography;
using AlexWoW.Cryptography;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;

namespace AlexWoW.Web.Services;

/// <summary>
/// Реализация <see cref="IAccountService"/> поверх <see cref="IAccountRepository"/> + SRP6.
/// Совпадает с серверной аутентификацией: соль/верификатор считаются ровно как в
/// <c>AccountCreator</c>/<c>PasswordReset</c>, поэтому пароль из панели работает в игре.
/// </summary>
public sealed class AccountService(IAccountRepository accounts) : IAccountService
{
    public async Task<RegisterResult> RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        var username = email.ToUpperInvariant();
        if (await accounts.AccountExistsAsync(username, ct))
            return RegisterResult.AlreadyExists;

        var (salt, verifier) = MakeCredentials(username, password);
        await accounts.CreateAccountAsync(username, salt, verifier, ct);
        return RegisterResult.Success;
    }

    public async Task<Account?> VerifyCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
        var username = email.ToUpperInvariant();
        var account = await accounts.GetAccountByUsernameAsync(username, ct);
        if (account is null)
            return null;
        return VerifierMatches(username, password, account) ? account : null;
    }

    public async Task<bool> ChangePasswordAsync(string email, string currentPassword, string newPassword,
        CancellationToken ct = default)
    {
        var username = email.ToUpperInvariant();
        var account = await accounts.GetAccountByUsernameAsync(username, ct);
        if (account is null || !VerifierMatches(username, currentPassword, account))
            return false;

        var (salt, verifier) = MakeCredentials(username, newPassword);
        await accounts.UpdatePasswordAsync(username, salt, verifier, ct);
        return true;
    }

    /// <summary>Сверяет пароль: пересчитывает верификатор по сохранённой соли и сравнивает (constant-time).</summary>
    private static bool VerifierMatches(string username, string password, Account account)
    {
        var verifier = Srp6.ToFixedLittleEndian(
            Srp6.CalculateVerifier(username, password, account.Salt), Srp6.KeyLength);
        return CryptographicOperations.FixedTimeEquals(verifier, account.Verifier);
    }

    /// <summary>Генерирует случайную соль и верификатор v = g^x mod N для пары username/password.</summary>
    private static (byte[] Salt, byte[] Verifier) MakeCredentials(string username, string password)
    {
        var salt = new byte[Srp6.SaltLength];
        RandomNumberGenerator.Fill(salt);
        var verifier = Srp6.ToFixedLittleEndian(
            Srp6.CalculateVerifier(username, password, salt), Srp6.KeyLength);
        return (salt, verifier);
    }
}
