using AlexWoW.Database.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelTeleport = AlexWoW.Database.Models.TeleportLocation;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// EF-репозиторий точек телепорта (таблица dev_teleport, БД alexwow_auth). SRP-часть DAL. Только чтение
/// (правка состава/координат — вручную в БД, по задумке dev-таблицы). Контекст из пула на операцию.
/// </summary>
public sealed class EfTeleportRepository(IDbContextFactory<AuthDbContext> factory) : ITeleportRepository
{
    public async Task<IReadOnlyList<ModelTeleport>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.TeleportLocations.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
        return [.. rows.Select(Map)];
    }

    public async Task<ModelTeleport?> GetByIdAsync(uint id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.TeleportLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : Map(row);
    }

    private static ModelTeleport Map(Entities.TeleportLocation x) => new()
    {
        Id = x.Id,
        SortOrder = x.SortOrder,
        Name = x.Name,
        Faction = x.Faction,
        Map = x.Map,
        Zone = x.Zone,
        X = x.X,
        Y = x.Y,
        Z = x.Z,
        O = x.O,
    };
}
