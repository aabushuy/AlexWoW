using AlexWoW.Database.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelRealm = AlexWoW.Database.Models.Realm;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// EF-репозиторий списка реалмов (таблица realmlist, БД alexwow_auth). SRP-часть DAL (#24).
/// Контекст из пула на операцию.
/// </summary>
public sealed class EfRealmRepository(IDbContextFactory<AuthDbContext> factory) : IRealmRepository
{
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
}
