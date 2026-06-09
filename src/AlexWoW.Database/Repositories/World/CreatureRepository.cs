using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>Существа БД мира (creature + creature_template). SRP-репозиторий (#25), Dapper read-only.</summary>
public sealed class CreatureRepository(string connectionString)
    : MangosRepositoryBase(connectionString), ICreatureRepository
{
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

    public async Task<long> CountCreaturesAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM creature;");
    }

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
              AND t.Name NOT LIKE '%[%'      -- дев/плейсхолдеры в имени где угодно: [DND]/[PH]/[UNUSED]
              -- TAR-тестовые тренеры/бистмастер: generic-имя без subname (у настоящих имя — личное)
              AND NOT (t.Name LIKE '% Trainer' AND COALESCE(t.SubName, '') = '')
              AND NOT (t.Name = 'Beastmaster' AND COALESCE(t.SubName, '') = '')
              AND COALESCE(t.SubName, '') NOT LIKE 'Arena %'   -- арена-NPC по subname
              AND NOT (t.Name LIKE '%Vendor%' AND COALESCE(t.SubName, '') = '') -- generic-вендоры без имени
              AND c.id NOT IN @excluded     -- кастомные арена-NPC CMaNGOS (по entry)
              -- Ивентовый контент (Ярмарка Новолуния и пр.): системы game_event у нас нет,
              -- иначе он спавнится ПОСТОЯННО — прячем спавны, активные только во время ивента (event>0).
              AND NOT EXISTS (SELECT 1 FROM game_event_creature gec WHERE gec.guid = c.guid AND gec.event > 0)
              AND c.position_x BETWEEN @minX AND @maxX
              AND c.position_y BETWEEN @minY AND @maxY
            LIMIT @limit;
            """,
            new { map, minX = x - range, maxX = x + range, minY = y - range, maxY = y + range, limit,
                  excluded = ExcludedCreatureEntries },
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<CreatureTemplateData?> GetCreatureTemplateAsync(uint entry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<CreatureTemplateData>(new CommandDefinition("""
            SELECT Entry, Name, SubName, DisplayId1, Faction, MinLevel, CreatureType, NpcFlags, UnitClass, Scale
            FROM creature_template WHERE Entry = @entry;
            """, new { entry }, cancellationToken: ct));
    }
}
