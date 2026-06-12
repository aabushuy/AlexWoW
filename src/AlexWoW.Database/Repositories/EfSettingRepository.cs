using AlexWoW.Database.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// EF-репозиторий key-value настроек сервера (таблица server_setting, БД alexwow_auth).
/// Контекст из пула на КАЖДУЮ операцию (singleton-safe). M8.6.
/// </summary>
public sealed class EfSettingRepository(IDbContextFactory<AuthDbContext> factory) : ISettingRepository
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ServerSettings.AsNoTracking()
            .Where(x => x.Key == key).Select(x => x.Value).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ServerSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value, ct);
    }
}
