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

    /// <summary>
    /// Кастомные/тестовые NPC CMaNGOS, которых нет в ретейле (арена-организаторы и пр.) —
    /// заспавнены в каждом городе и часто «парят». Не показываем игрокам.
    /// </summary>
    private static readonly int[] ExcludedCreatureEntries =
    {
        26012, // Arena Organizer
        26075, // Paymaster
        26760, // Fight Promoter (Arena Battlemaster's Assistant)
    };

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
              AND t.Name NOT LIKE '[%'       -- дев/плейсхолдеры: [DND], [PH], [UNUSED]…
              -- TAR-тестовые тренеры/бистмастер: generic-имя без subname (у настоящих имя — личное)
              AND NOT (t.Name LIKE '% Trainer' AND COALESCE(t.SubName, '') = '')
              AND NOT (t.Name = 'Beastmaster' AND COALESCE(t.SubName, '') = '')
              AND c.id NOT IN @excluded     -- кастомные арена-NPC CMaNGOS
              AND c.position_x BETWEEN @minX AND @maxX
              AND c.position_y BETWEEN @minY AND @maxY
            LIMIT @limit;
            """,
            new { map, minX = x - range, maxX = x + range, minY = y - range, maxY = y + range, limit,
                  excluded = ExcludedCreatureEntries },
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

    /// <summary>Видимые гейм-объекты на карте в квадрате ±range (только с моделью: displayId &lt;&gt; 0).</summary>
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
              AND t.name NOT LIKE '[%'       -- дев/плейсхолдеры
              AND g.position_x BETWEEN @minX AND @maxX
              AND g.position_y BETWEEN @minY AND @maxY
            LIMIT @limit;
            """,
            new { map, minX = x - range, maxX = x + range, minY = y - range, maxY = y + range, limit },
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Шаблон гейм-объекта по entry (для CMSG_GAMEOBJECT_QUERY).</summary>
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
