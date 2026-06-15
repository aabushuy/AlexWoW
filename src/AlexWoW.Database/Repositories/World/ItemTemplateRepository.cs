using System.Globalization;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>Шаблоны предметов БД мира (item_template). SRP-репозиторий (#25), Dapper read-only.</summary>
public sealed class ItemTemplateRepository(string connectionString)
    : MangosRepositoryBase(connectionString), IItemTemplateRepository
{
    public async Task<IReadOnlyDictionary<uint, (uint DisplayId, byte InventoryType)>> GetItemDisplaysAsync(
        IReadOnlyCollection<uint> entries, CancellationToken ct = default)
    {
        var result = new Dictionary<uint, (uint, byte)>();
        if (entries.Count == 0)
            return result;

        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync(new CommandDefinition(
            "SELECT entry AS Entry, displayid AS DisplayId, InventoryType FROM item_template WHERE entry IN @entries;",
            new { entries }, cancellationToken: ct));
        foreach (var row in rows)
        {
            var d = (IDictionary<string, object>)row;
            var entry = Convert.ToUInt32(d["Entry"], CultureInfo.InvariantCulture);
            var displayId = Convert.ToUInt32(d["DisplayId"], CultureInfo.InvariantCulture);
            var invType = Convert.ToByte(d["InventoryType"], CultureInfo.InvariantCulture);
            result[entry] = (displayId, invType);
        }
        return result;
    }

    public async Task<IReadOnlyDictionary<uint, ItemBagInfo>> GetItemBagInfoAsync(
        IReadOnlyCollection<uint> entries, CancellationToken ct = default)
    {
        var result = new Dictionary<uint, ItemBagInfo>();
        if (entries.Count == 0)
            return result;

        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync(new CommandDefinition(
            "SELECT entry AS Entry, class AS Class, ContainerSlots, MaxDurability FROM item_template WHERE entry IN @entries;",
            new { entries }, cancellationToken: ct));
        foreach (var row in rows)
        {
            var d = (IDictionary<string, object>)row;
            var entry = Convert.ToUInt32(d["Entry"], CultureInfo.InvariantCulture);
            result[entry] = new ItemBagInfo(
                Convert.ToUInt32(d["Class"], CultureInfo.InvariantCulture),
                Convert.ToUInt32(d["ContainerSlots"], CultureInfo.InvariantCulture),
                Convert.ToUInt32(d["MaxDurability"], CultureInfo.InvariantCulture));
        }
        return result;
    }

    public async Task<ItemTemplateData?> GetItemTemplateAsync(uint entry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var row = (IDictionary<string, object>?)await db.QueryFirstOrDefaultAsync(new CommandDefinition(
            "SELECT * FROM item_template WHERE entry = @entry;", new { entry }, cancellationToken: ct));
        return row is null ? null : ItemTemplateMapper.Map(row);
    }
}
