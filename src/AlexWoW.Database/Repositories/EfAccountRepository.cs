using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Entities;
using Microsoft.EntityFrameworkCore;
using ModelAccount = AlexWoW.Database.Models.Account;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// EF-репозиторий аккаунтов (таблица account, БД alexwow_auth) — только операции с аккаунтами.
/// SRP-часть DAL (#24): реалмы — в <see cref="EfRealmRepository"/>, инициализация схемы — в
/// <see cref="AuthSchemaInitializer"/>. Контекст из пула на КАЖДУЮ операцию (singleton-safe).
/// </summary>
public sealed class EfAccountRepository(IDbContextFactory<AuthDbContext> factory) : IAccountRepository
{
    public async Task<ModelAccount?> GetAccountByUsernameAsync(string username, CancellationToken ct = default)
    {
        var u = username.ToUpperInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Username == u, ct);
        return a is null ? null : ToModel(a);
    }

    public async Task<ModelAccount?> GetAccountByEmailAsync(string email, CancellationToken ct = default)
    {
        var e = email.ToLowerInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Email == e, ct);
        return a is null ? null : ToModel(a);
    }

    public async Task<bool> AccountExistsAsync(string username, CancellationToken ct = default)
    {
        var u = username.ToUpperInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Accounts.AsNoTracking().AnyAsync(x => x.Username == u, ct);
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
    {
        var e = email.ToLowerInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Accounts.AsNoTracking().AnyAsync(x => x.Email == e, ct);
    }

    public async Task CreateAccountAsync(string username, byte[] salt, byte[] verifier, string? email = null,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Accounts.Add(new Account
        {
            Username = username.ToUpperInvariant(),
            Email = email?.ToLowerInvariant(),
            Salt = salt,
            Verifier = verifier,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetAllUsernamesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Accounts.AsNoTracking().Select(x => x.Username).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetAdminUsernamesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Accounts.AsNoTracking().Where(x => x.IsAdmin != 0)
            .OrderBy(x => x.Username).Select(x => x.Username).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AlexWoW.Database.Models.AccountSummary>> GetAccountsWithCharCountsAsync(
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // Коррелированный подзапрос на число персонажей — без навигации Account→Characters в модели.
        return await db.Accounts.AsNoTracking()
            .OrderBy(a => a.Username)
            .Select(a => new AlexWoW.Database.Models.AccountSummary
            {
                Id = a.Id,
                Username = a.Username,
                Email = a.Email,
                CreatedAt = a.CreatedAt,
                CharacterCount = db.Characters.Count(c => c.AccountId == a.Id),
            })
            .ToListAsync(ct);
    }

    public async Task<ModelAccount?> GetAccountByIdAsync(uint id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? null : ToModel(a);
    }

    public async Task UpdatePasswordAsync(string username, byte[] salt, byte[] verifier, CancellationToken ct = default)
    {
        var u = username.ToUpperInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        // Сброс session_key форсит ре-логин.
        await db.Accounts.Where(x => x.Username == u).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Salt, salt)
            .SetProperty(x => x.Verifier, verifier)
            .SetProperty(x => x.SessionKey, (byte[]?)null), ct);
    }

    public async Task SetSessionKeyAsync(uint accountId, byte[] sessionKey, string? ip, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Accounts.Where(x => x.Id == accountId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.SessionKey, sessionKey)
            .SetProperty(x => x.LastIp, ip), ct);
    }

    public async Task<int> SetAdminAsync(string username, bool isAdmin, CancellationToken ct = default)
    {
        var u = username.ToUpperInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Accounts.Where(x => x.Username == u)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsAdmin, (byte)(isAdmin ? 1 : 0)), ct);
    }

    private static ModelAccount ToModel(Account a) => new()
    {
        Id = a.Id,
        Username = a.Username,
        Email = a.Email,
        Salt = a.Salt,
        Verifier = a.Verifier,
        SessionKey = a.SessionKey,
        LastIp = a.LastIp,
        IsAdmin = a.IsAdmin != 0,
        CreatedAt = a.CreatedAt,
    };
}
