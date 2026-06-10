using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Entities;
using Microsoft.EntityFrameworkCore;
using ModelInventoryItem = AlexWoW.Database.Models.InventoryItem;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// EF-репозиторий инвентаря персонажа (таблица character_items, БД alexwow_auth).
/// SRP-часть DAL (#24). Контекст из пула на операцию.
/// </summary>
public sealed class EfInventoryRepository(IDbContextFactory<AuthDbContext> factory) : IInventoryRepository
{
    public async Task<bool> HasItemsAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.CharacterItems.AsNoTracking().AnyAsync(x => x.OwnerGuid == ownerGuid, ct);
    }

    public async Task<IReadOnlyList<ModelInventoryItem>> GetItemsAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.CharacterItems.AsNoTracking()
            .Where(x => x.OwnerGuid == ownerGuid).OrderBy(x => x.Bag).ThenBy(x => x.Slot).ToListAsync(ct);
        return rows.Select(x => new ModelInventoryItem
        {
            ItemGuid = x.ItemGuid,
            OwnerGuid = x.OwnerGuid,
            ItemEntry = x.ItemEntry,
            Bag = x.Bag,
            Slot = x.Slot,
            StackCount = x.StackCount,
        }).ToList();
    }

    public async Task<uint> AddItemAsync(uint ownerGuid, uint itemEntry, byte bag, byte slot,
        uint stackCount = 1, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = new CharacterItem
        {
            OwnerGuid = ownerGuid,
            ItemEntry = itemEntry,
            Bag = bag,
            Slot = slot,
            StackCount = stackCount,
        };
        db.CharacterItems.Add(e);
        await db.SaveChangesAsync(ct);
        return e.ItemGuid;
    }

    public async Task RemoveItemAsync(uint itemGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.CharacterItems.Where(x => x.ItemGuid == itemGuid).ExecuteDeleteAsync(ct);
    }

    public async Task MoveItemAsync(uint itemGuid, byte bag, byte slot, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.CharacterItems.Where(x => x.ItemGuid == itemGuid).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Bag, bag)
            .SetProperty(x => x.Slot, slot), ct);
    }

    public async Task SetItemStackAsync(uint itemGuid, uint stackCount, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.CharacterItems.Where(x => x.ItemGuid == itemGuid)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StackCount, stackCount), ct);
    }
}
