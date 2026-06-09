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
    /// Кладёт <paramref name="qty"/> предметов в рюкзак (M6.6 + M7 #20): сперва ДОЛИВАЕТ существующие
    /// стопки того же предмета до лимита (`item_template.stackable`), затем остаток — в новые слоты
    /// (по лимиту на слот). Persist (character_items) + сессия + апдейты клиенту (stack/create/слот).
    /// Возвращает последний затронутый предмет или null, если ничего не поместилось (нет места).
    /// </summary>
    public static async Task<InventoryItem?> TryGiveAsync(WorldSession session, uint itemEntry, uint qty, CancellationToken ct)
    {
        if (qty == 0)
            qty = 1;
        var ownerGuid = session.InWorldGuid;

        // Лимит стопки из шаблона (нестакающийся → 1). БД мира недоступна — считаем нестакающимся.
        uint maxStack = 1;
        try
        {
            var t = await session.WorldDb.GetItemTemplateAsync(itemEntry, ct);
            if (t is not null)
                maxStack = (uint)Math.Max(1, t.Stackable);
        }
        catch { /* без шаблона — кладём как нестакающийся */ }

        var remaining = qty;
        InventoryItem? last = null;

        // 1) Долить существующие стопки того же предмета (только для стакающихся).
        if (maxStack > 1)
        {
            foreach (var stack in session.Inventory.Where(i => i.ItemEntry == itemEntry && i.StackCount < maxStack).ToList())
            {
                if (remaining == 0)
                    break;
                var add = Math.Min(remaining, maxStack - stack.StackCount);
                stack.StackCount += add;
                remaining -= add;
                await session.Characters.SetItemStackAsync(stack.ItemGuid, stack.StackCount, ct);
                await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                    ItemObject.BuildStackUpdate(ItemObject.ItemGuid(stack.ItemGuid), stack.StackCount), ct);
                last = stack;
            }
        }

        // 2) Остаток — в новые слоты (по maxStack на слот).
        while (remaining > 0)
        {
            var slot = FreeBackpackSlot(session);
            if (slot < 0)
                break; // нет места — остаток не выдан (bag-full → почта, M7 #14)
            var portion = Math.Min(remaining, maxStack);
            var itemLow = await session.Characters.AddItemAsync(ownerGuid, itemEntry, InventorySlots.MainBag, (byte)slot, portion, ct);
            var item = new InventoryItem
            {
                ItemGuid = itemLow, OwnerGuid = ownerGuid, ItemEntry = itemEntry,
                Bag = InventorySlots.MainBag, Slot = (byte)slot, StackCount = portion,
            };
            session.Inventory.Add(item);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject, ItemObject.BuildItemsCreate(new[] { item }, ownerGuid), ct);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                PlayerSpawn.BuildInvSlotUpdate(ownerGuid, slot, ItemObject.ItemGuid(itemLow)), ct);
            remaining -= portion;
            last = item;
        }

        if (last is not null)
            await QuestHandlers.OnItemGainedAsync(session, itemEntry, ct); // M6.10: зачёт item-целей квестов
        return last;
    }
}
