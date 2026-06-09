using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>Ассортимент вендоров БД мира (npc_vendor[_template] ⨝ item_template). SRP-репозиторий (#25).</summary>
public sealed class VendorRepository(string connectionString)
    : MangosRepositoryBase(connectionString), IVendorRepository
{
    public async Task<IReadOnlyList<VendorItem>> GetVendorItemsAsync(uint entry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        // Ассортимент может быть задан напрямую (npc_vendor по entry существа) ИЛИ через шаблон
        // (npc_vendor_template по creature_template.VendorTemplateId) — объединяем оба источника.
        var rows = await db.QueryAsync<VendorItem>(new CommandDefinition("""
            SELECT v.slot AS Slot, v.item AS ItemId, v.maxcount AS MaxCount,
                   t.BuyPrice AS BuyPrice, t.displayid AS DisplayId, t.MaxDurability AS MaxDurability,
                   t.BuyCount AS BuyCount, t.name AS Name, t.stackable AS Stackable
            FROM (
                SELECT slot, item, maxcount, ExtendedCost, condition_id
                FROM npc_vendor WHERE entry = @entry
                UNION ALL
                SELECT nvt.slot, nvt.item, nvt.maxcount, nvt.ExtendedCost, nvt.condition_id
                FROM npc_vendor_template nvt
                JOIN creature_template ct ON ct.VendorTemplateId = nvt.entry
                WHERE ct.entry = @entry AND ct.VendorTemplateId <> 0
            ) v
            JOIN item_template t ON t.entry = v.item
            WHERE v.ExtendedCost = 0 AND COALESCE(v.condition_id, 0) = 0
            ORDER BY v.slot;
            """, new { entry }, cancellationToken: ct));
        return rows.AsList();
    }
}
