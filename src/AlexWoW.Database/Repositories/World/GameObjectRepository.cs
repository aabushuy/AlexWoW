using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>Гейм-объекты БД мира (gameobject + gameobject_template). SRP-репозиторий (#25), Dapper read-only.</summary>
public sealed class GameObjectRepository(string connectionString)
    : MangosRepositoryBase(connectionString), IGameObjectRepository
{
    public async Task<IReadOnlyList<GameObjectSpawnData>> GetGameObjectsNearAsync(
        uint map, float x, float y, float range, int limit, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<GameObjectSpawnData>(new CommandDefinition("""
            SELECT g.guid AS Guid, g.id AS Entry,
                   CAST(g.position_x AS DOUBLE) AS X, CAST(g.position_y AS DOUBLE) AS Y,
                   CAST(g.position_z AS DOUBLE) AS Z, CAST(g.orientation AS DOUBLE) AS O,
                   CAST(g.rotation0 AS DOUBLE) AS Rot0, CAST(g.rotation1 AS DOUBLE) AS Rot1,
                   CAST(g.rotation2 AS DOUBLE) AS Rot2, CAST(g.rotation3 AS DOUBLE) AS Rot3,
                   t.name AS Name, t.type AS Type, t.displayId AS DisplayId,
                   t.faction AS Faction, t.flags AS Flags, t.size AS Size
            FROM gameobject g
            JOIN gameobject_template t ON t.entry = g.id
            WHERE g.map = @map
              AND (g.spawnMask & 1) = 1
              AND t.displayId <> 0
              AND t.name NOT LIKE '%[%'      -- дев/плейсхолдеры в имени где угодно
              -- Ивентовый контент (венки Новогодья, тенты/декор Ярмарки Новолуния и пр.) — прячем event>0.
              AND NOT EXISTS (SELECT 1 FROM game_event_gameobject geg WHERE geg.guid = g.guid AND geg.event > 0)
              AND g.position_x BETWEEN @minX AND @maxX
              AND g.position_y BETWEEN @minY AND @maxY
            LIMIT @limit;
            """,
            new { map, minX = x - range, maxX = x + range, minY = y - range, maxY = y + range, limit },
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<GameObjectTemplateData?> GetGameObjectTemplateAsync(uint entry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<GameObjectTemplateData>(new CommandDefinition("""
            SELECT entry AS Entry, type AS Type, displayId AS DisplayId, name AS Name,
                   IconName, castBarCaption AS CastBarCaption, unk1 AS Unk1, size AS Size
            FROM gameobject_template WHERE entry = @entry;
            """, new { entry }, cancellationToken: ct));
    }
}
