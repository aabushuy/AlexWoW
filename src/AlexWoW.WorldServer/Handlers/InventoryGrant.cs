using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Общая выдача предмета в рюкзак (M6.6): первый свободный слот → БД + сессия + создание объекта у
/// клиента + привязка к слоту. Используется лутом (M6.6) и торговлей (M6.2).
/// </summary>
public static class InventoryGrant
{
    /// <summary>Первый свободный слот рюкзака (23..38) основного контейнера; -1 если места нет.</summary>
    public static int FreeBackpackSlot(WorldSession session)
    {
        var taken = new HashSet<byte>();
        foreach (var i in session.Inventory)
            if (i.Bag == InventorySlots.MainBag)
                taken.Add(i.Slot);
        for (var s = InventorySlots.BackpackStart; s < InventorySlots.BackpackEnd; s++)
            if (!taken.Contains((byte)s))
                return s;
        return -1;
    }

    /// <summary>
    /// Кладёт предмет в первый свободный слот рюкзака: persist (character_items) + сессия + создание
    /// item-объекта у клиента + привязка к слоту. Возвращает созданный предмет или null, если места нет.
    /// </summary>
    public static async Task<InventoryItem?> TryGiveAsync(WorldSession session, uint itemEntry, uint qty, CancellationToken ct)
    {
        var slot = FreeBackpackSlot(session);
        if (slot < 0)
            return null;

        var ownerGuid = session.InWorldGuid;
        var itemLow = await session.Characters.AddItemAsync(ownerGuid, itemEntry, InventorySlots.MainBag, (byte)slot, qty, ct);
        var item = new InventoryItem
        {
            ItemGuid = itemLow, OwnerGuid = ownerGuid, ItemEntry = itemEntry,
            Bag = InventorySlots.MainBag, Slot = (byte)slot, StackCount = qty,
        };
        session.Inventory.Add(item);

        await session.SendAsync(WorldOpcode.SmsgUpdateObject, ItemObject.BuildItemsCreate(new[] { item }, ownerGuid), ct);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildInvSlotUpdate(ownerGuid, slot, ItemObject.ItemGuid(itemLow)), ct);
        return item;
    }
}
