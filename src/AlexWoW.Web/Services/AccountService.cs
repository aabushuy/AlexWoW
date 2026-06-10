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
    public async Task<RegisterResult> RegisterAsync(string email, string accountName, string password,
        CancellationToken ct = default)
    {
        var username = accountName.ToUpperInvariant();   // игровой логин = SRP-идентичность
        if (await accounts.AccountExistsAsync(username, ct))
            return RegisterResult.AccountNameTaken;
        if (await accounts.EmailExistsAsync(email, ct))
            return RegisterResult.EmailTaken;

        var (salt, verifier) = MakeCredentials(username, password);
        await accounts.CreateAccountAsync(username, salt, verifier, email, ct);
        return RegisterResult.Success;
    }

    public async Task<Account?> VerifyCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
        var account = await accounts.GetAccountByEmailAsync(email, ct);
        if (account is null)
            return null;
        // SRP считается по игровому имени аккаунта, не по email.
        return VerifierMatches(account.Username, password, account) ? account : null;
    }

    public async Task<bool> ChangePasswordAsync(string email, string currentPassword, string newPassword,
        CancellationToken ct = default)
    {
        var account = await accounts.GetAccountByEmailAsync(email, ct);
        if (account is null || !VerifierMatches(account.Username, currentPassword, account))
            return false;

        var (salt, verifier) = MakeCredentials(account.Username, newPassword);
        await accounts.UpdatePasswordAsync(account.Username, salt, verifier, ct);
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
