using System.Globalization;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>Лут существ БД мира (creature_template + creature_loot_template). SRP-репозиторий (#25).</summary>
public sealed class LootRepository(string connectionString)
    : MangosRepositoryBase(connectionString), ILootRepository
{
    public async Task<CreatureLootData?> GetCreatureLootAsync(uint creatureEntry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);

        var head = await db.QuerySingleOrDefaultAsync(new CommandDefinition(
            "SELECT LootId, MinLootGold, MaxLootGold FROM creature_template WHERE Entry = @creatureEntry;",
            new { creatureEntry }, cancellationToken: ct));
        if (head is null)
            return null;
        var h = (IDictionary<string, object>)head;
        var lootId = Convert.ToUInt32(h["LootId"], CultureInfo.InvariantCulture);
        var minGold = Convert.ToUInt32(h["MinLootGold"], CultureInfo.InvariantCulture);
        var maxGold = Convert.ToUInt32(h["MaxLootGold"], CultureInfo.InvariantCulture);

        List<CreatureLootEntry> drops = [];
        if (lootId != 0)
        {
            var rows = await db.QueryAsync<CreatureLootEntry>(new CommandDefinition("""
                SELECT lt.item AS ItemId, lt.ChanceOrQuestChance AS Chance,
                       lt.mincountOrRef AS MinCount, lt.maxcount AS MaxCount, it.displayid AS DisplayId
                FROM creature_loot_template lt
                JOIN item_template it ON it.entry = lt.item
                WHERE lt.entry = @lootId AND lt.ChanceOrQuestChance <> 0 AND lt.mincountOrRef > 0;
                """, new { lootId }, cancellationToken: ct));
            drops = rows.AsList();
        }

        if (maxGold == 0 && drops.Count == 0)
            return null; // нечего лутать
        return new CreatureLootData { MinGold = minGold, MaxGold = maxGold, Drops = drops };
    }
}
