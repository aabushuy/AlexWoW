using AlexWoW.Database.Models;
using Dapper;
using MySqlConnector;

namespace AlexWoW.Database;

/// <summary>
/// Доступ к статической БД мира (дамп CMaNGOS-WotLK: creature, creature_template …).
/// Только чтение. Координаты в дампе — decimal(40,20); приводим к DOUBLE на стороне MySQL.
/// </summary>
public sealed class WorldDatabase(string connectionString)
{
    private readonly string _connectionString = connectionString;

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    /// <summary>Проверка доступности БД мира при старте (есть ли таблица creature).</summary>
    public async Task<long> CountCreaturesAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM creature;");
    }

    /// <summary>Спавны существ на карте в квадрате ±range от точки (грубая зона видимости).</summary>
    public async Task<IReadOnlyList<CreatureSpawnData>> GetCreaturesNearAsync(
        uint map, float x, float y, float range, int limit, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<CreatureSpawnData>(new CommandDefinition("""
            SELECT c.guid AS Guid, c.id AS Entry,
                   CAST(c.position_x AS DOUBLE) AS X, CAST(c.position_y AS DOUBLE) AS Y,
                   CAST(c.position_z AS DOUBLE) AS Z, CAST(c.orientation AS DOUBLE) AS O,
                   t.Name, t.SubName,
                   t.DisplayId1, t.DisplayId2, t.DisplayId3, t.DisplayId4,
                   t.Faction, t.MinLevel, t.MaxLevel, t.CreatureType, t.NpcFlags, t.UnitClass, t.Scale
            FROM creature c
            JOIN creature_template t ON t.Entry = c.id
            WHERE c.map = @map
              AND (c.spawnMask & 1) = 1
              AND t.Name NOT LIKE '[DND]%'   -- дев/QA-NPC CMaNGOS (TAR-пьедесталы и т.п.)
              AND c.position_x BETWEEN @minX AND @maxX
              AND c.position_y BETWEEN @minY AND @maxY
            LIMIT @limit;
            """,
            new { map, minX = x - range, maxX = x + range, minY = y - range, maxY = y + range, limit },
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Шаблон существа по entry (для CMSG_CREATURE_QUERY).</summary>
    public async Task<CreatureTemplateData?> GetCreatureTemplateAsync(uint entry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<CreatureTemplateData>(new CommandDefinition("""
            SELECT Entry, Name, SubName, DisplayId1, Faction, MinLevel, CreatureType, NpcFlags, UnitClass, Scale
            FROM creature_template WHERE Entry = @entry;
            """, new { entry }, cancellationToken: ct));
    }
}
