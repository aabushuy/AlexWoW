using AlexWoW.Cryptography;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.Web.Services;

namespace AlexWoW.Web.Tests;

/// <summary>
/// Админ-сброс пароля (M8.9): пишет SRP6 соль+верификатор для «123456» по игровому имени, без проверки
/// текущего. Верификатор должен совпадать с серверным расчётом, иначе пароль не подойдёт в игре.
/// </summary>
public sealed class AdminResetPasswordTests
{
    [Fact]
    public async Task Reset_writes_verifier_matching_srp6_for_default_password()
    {
        var repo = new FakeAccountRepository("TEST");
        var service = new AccountService(repo);

        var ok = await service.AdminResetPasswordAsync("test");

        Assert.True(ok);
        Assert.NotNull(repo.LastSalt);
        Assert.NotNull(repo.LastVerifier);
        Assert.Equal(Srp6.SaltLength, repo.LastSalt!.Length);

        // Пересчёт верификатора по той же соли и каноничному имени — как делает сервер на входе.
        var expected = Srp6.ToFixedLittleEndian(
            Srp6.CalculateVerifier("TEST", AccountService.DefaultResetPassword, repo.LastSalt!), Srp6.KeyLength);
        Assert.Equal(expected, repo.LastVerifier);
    }

    [Fact]
    public async Task Reset_returns_false_for_unknown_account()
    {
        var repo = new FakeAccountRepository(existingUsername: null);
        var service = new AccountService(repo);

        Assert.False(await service.AdminResetPasswordAsync("ghost"));
        Assert.Null(repo.LastVerifier);
    }

    /// <summary>Фейк: знает один аккаунт и фиксирует записанные при сбросе соль/верификатор.</summary>
    private sealed class FakeAccountRepository(string? existingUsername) : IAccountRepository
    {
        public byte[]? LastSalt { get; private set; }
        public byte[]? LastVerifier { get; private set; }

        public Task<Account?> GetAccountByUsernameAsync(string username, CancellationToken ct = default)
        {
            if (existingUsername is null || !string.Equals(username, existingUsername, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<Account?>(null);
            return Task.FromResult<Account?>(new Account
            {
                Id = 1,
                Username = existingUsername,
                Salt = new byte[Srp6.SaltLength],
                Verifier = new byte[Srp6.KeyLength],
            });
        }

        public Task UpdatePasswordAsync(string username, byte[] salt, byte[] verifier, CancellationToken ct = default)
        {
            LastSalt = salt;
            LastVerifier = verifier;
            return Task.CompletedTask;
        }

        // Не используются в этих тестах.
        public Task<Account?> GetAccountByEmailAsync(string email, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> AccountExistsAsync(string username, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CreateAccountAsync(string username, byte[] salt, byte[] verifier, string? email = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetAllUsernamesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetAdminUsernamesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<AccountSummary>> GetAccountsWithCharCountsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Account?> GetAccountByIdAsync(uint id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetSessionKeyAsync(uint accountId, byte[] sessionKey, string? ip, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> SetAdminAsync(string username, bool isAdmin, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
