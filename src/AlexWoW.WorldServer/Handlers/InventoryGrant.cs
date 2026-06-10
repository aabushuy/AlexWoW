using AlexWoW.Common.Network;
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
    /// <summary>Сколько предметов entry в сумках игрока (item-цели квестов, реагенты крафта).</summary>
    public static uint CountItem(WorldSession session, uint itemEntry)
    {
        uint total = 0;
        foreach (var it in session.Inventory)
            if (it.ItemEntry == itemEntry)
                total += it.StackCount;
        return total;
    }

    /// <summary>
    /// Списывает <paramref name="count"/> предметов entry из сумок (квест-предметы при сдаче M6.10,
    /// реагенты крафта M11.3): целые предметы удаляет (DestroyObject + очистка слота), частичную стопку
    /// уменьшает (ITEM_FIELD_STACK_COUNT), с персистом.
    /// </summary>
    public static async Task ConsumeAsync(WorldSession session, uint itemEntry, uint count, CancellationToken ct)
    {
        var ownerGuid = session.InWorldGuid;
        var remaining = count;
        foreach (var item in session.Inventory.Where(i => i.ItemEntry == itemEntry).ToList())
        {
            if (remaining == 0)
                break;
            if (item.StackCount <= remaining)
            {
                remaining -= item.StackCount;
                session.Inventory.Remove(item);
                await session.Items.RemoveItemAsync(item.ItemGuid, ct);
                await session.SendAsync(WorldOpcode.SmsgDestroyObject,
                    new ByteWriter(9).UInt64(ItemObject.ItemGuid(item.ItemGuid)).UInt8(0).ToArray(), ct);
                if (item.Bag == InventorySlots.MainBag)
                    await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                        PlayerSpawn.BuildInvSlotUpdate(ownerGuid, item.Slot, 0), ct);
            }
            else
            {
                item.StackCount -= remaining;
                remaining = 0;
                await session.Items.SetItemStackAsync(item.ItemGuid, item.StackCount, ct);
                await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                    ItemObject.BuildStackUpdate(ItemObject.ItemGuid(item.ItemGuid), item.StackCount), ct);
            }
        }
    }

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
            {
                maxStack = (uint)Math.Max(1, t.Stackable);
                // M6.13: запомнить bag-info — чтобы выданный предмет-сумка создался как TYPEID_CONTAINER.
                session.ItemBagInfo[itemEntry] = new ItemBagInfo(t.Class, t.ContainerSlots, t.MaxDurability);
            }
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
                await session.Items.SetItemStackAsync(stack.ItemGuid, stack.StackCount, ct);
                await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                    ItemObject.BuildStackUpdate(ItemObject.ItemGuid(stack.ItemGuid), stack.StackCount), ct);
                last = stack;
            }
        }

        // 2) Остаток — в новые слоты (по maxStack на слот): сперва рюкзак, затем надетые сумки (M6.13).
        while (remaining > 0)
        {
            if (BagInventory.FirstFreeStoreSlot(session) is not { } pos)
                break; // нет места — остаток не выдан (bag-full → почта, M7 #14)
            var portion = Math.Min(remaining, maxStack);
            var itemLow = await session.Items.AddItemAsync(ownerGuid, itemEntry, (byte)pos.Bag, (byte)pos.Slot, portion, ct);
            var item = new InventoryItem
            {
                ItemGuid = itemLow, OwnerGuid = ownerGuid, ItemEntry = itemEntry,
                Bag = (byte)pos.Bag, Slot = (byte)pos.Slot, StackCount = portion,
            };
            session.Inventory.Add(item);
            var itemGuid = ItemObject.ItemGuid(itemLow);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject, ItemObject.BuildItemsCreate(new[] { item }, ownerGuid, session.ItemBagInfo), ct);
            // Уведомить о позиции: осн. контейнер — поле игрока; внутри сумки — слот сумки + CONTAINED.
            if (pos.Bag == InventorySlots.MainBag)
                await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                    PlayerSpawn.BuildInvSlotUpdate(ownerGuid, pos.Slot, itemGuid), ct);
            else
            {
                var bagGuid = BagInventory.BagGuid(session, pos.Bag);
                await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                    ContainerObject.BuildSlotUpdate(bagGuid, pos.Slot, itemGuid), ct);
                await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                    ItemObject.BuildContainedUpdate(itemGuid, bagGuid), ct);
            }
            remaining -= portion;
            last = item;
        }

        if (last is not null)
            await QuestHandlers.OnItemGainedAsync(session, itemEntry, ct); // M6.10: зачёт item-целей квестов
        return last;
    }
}
