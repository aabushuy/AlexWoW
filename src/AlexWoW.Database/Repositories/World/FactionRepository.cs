using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>Реакции фракций БД мира (faction_template из FactionTemplate.dbc). SRP-репозиторий (#25).</summary>
public sealed class FactionRepository(string connectionString)
    : MangosRepositoryBase(connectionString), IFactionRepository
{
    public async Task<IReadOnlyList<FactionTemplateRow>> GetFactionTemplatesAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<FactionTemplateRow>(new CommandDefinition(
            "SELECT id AS Id, faction AS Faction, ourMask AS OurMask, friendMask AS FriendMask, hostileMask AS HostileMask, "
            + "enemy1 AS Enemy1, enemy2 AS Enemy2, enemy3 AS Enemy3, enemy4 AS Enemy4, "
            + "friend1 AS Friend1, friend2 AS Friend2, friend3 AS Friend3, friend4 AS Friend4 FROM faction_template;",
            cancellationToken: ct));
        return rows.AsList();
    }
}
