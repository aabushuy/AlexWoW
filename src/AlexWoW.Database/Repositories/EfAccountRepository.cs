using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Entities;
using Microsoft.EntityFrameworkCore;
using ModelAccount = AlexWoW.Database.Models.Account;
using ModelRealm = AlexWoW.Database.Models.Realm;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// EF Core реализация <see cref="IAccountRepository"/> поверх <see cref="AuthDbContext"/> (БД alexwow_auth).
/// Срез 3 рефактора DAL (#23): заменяет Dapper-путь auth. Контекст берётся из пула на КАЖДУЮ операцию
/// (<see cref="IDbContextFactory{TContext}"/>) — это потокобезопасно (репозиторий — singleton, а сессии
/// многопоточны) и повторяет прежнюю модель «короткое подключение на запрос» Dapper.
/// </summary>
public sealed class EfAccountRepository(IDbContextFactory<AuthDbContext> factory) : IAccountRepository
{
    /// <summary>Применяет EF-миграции (создаёт схему на чистой БД; на проде — no-op после baseline) и
    /// сидирует реалм по умолчанию, если список пуст. Заменяет ручной EnsureSchemaAsync.</summary>
    public async Task EnsureSchemaAsync(ModelRealm defaultRealm, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
        if (!await db.Realms.AnyAsync(ct))
        {
            db.Realms.Add(new Realm
            {
                Name = defaultRealm.Name,
                Address = defaultRealm.Address,
                Port = defaultRealm.Port,
                Type = defaultRealm.Type,
                Flags = defaultRealm.Flags,
                Timezone = defaultRealm.Timezone,
                Population = defaultRealm.Population,
            });
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<ModelAccount?> GetAccountByUsernameAsync(string username, CancellationToken ct = default)
    {
        var u = username.ToUpperInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Username == u, ct);
        return a is null ? null : ToModel(a);
    }

    public async Task<bool> AccountExistsAsync(string username, CancellationToken ct = default)
    {
        var u = username.ToUpperInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Accounts.AsNoTracking().AnyAsync(x => x.Username == u, ct);
    }

    public async Task CreateAccountAsync(string username, byte[] salt, byte[] verifier, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Accounts.Add(new Account
        {
            Username = username.ToUpperInvariant(),
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

    public async Task UpdatePasswordAsync(string username, byte[] salt, byte[] verifier, CancellationToken ct = default)
    {
        var u = username.ToUpperInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        // Сброс session_key форсит ре-логин (как в Dapper-версии).
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

    public async Task<IReadOnlyList<ModelRealm>> GetRealmsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Realms.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        return rows.Select(x => new ModelRealm
        {
            Id = x.Id, Name = x.Name, Address = x.Address, Port = x.Port,
            Type = x.Type, Flags = x.Flags, Timezone = x.Timezone, Population = x.Population,
        }).ToList();
    }

    private static ModelAccount ToModel(Account a) => new()
    {
        Id = a.Id,
        Username = a.Username,
        Salt = a.Salt,
        Verifier = a.Verifier,
        SessionKey = a.SessionKey,
        LastIp = a.LastIp,
        IsAdmin = a.IsAdmin != 0,
    };
}
