using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Адресация надетых сумок и поиск свободного места (M6.13). Позиция предмета — пара (bag, slot):
/// bag=255 — основной контейнер (экипировка 0..18, bag-слоты 19..22, рюкзак 23..38); bag=19..22 —
/// ВНУТРИ надетой сумки (slot = индекс 0..ContainerSlots-1). Ёмкость сумки берётся из кэша
/// <see cref="Net.SessionState.SessionInventoryState.ItemBagInfo"/> (батч при входе). Без вложенности сумок (M6-объём).
/// </summary>
public static class BagInventory
{
    /// <summary>Надетые сумки: (bag-слот 19..22, предмет-сумка, ёмкость). Только с ёмкостью > 0.</summary>
    public static IEnumerable<(int BagSlot, InventoryItem Bag, uint Capacity)> EquippedBags(WorldSession session)
    {
        for (var s = InventorySlots.BagSlotStart; s < InventorySlots.BagSlotEnd; s++)
        {
            var bag = MainItemAt(session, s);
            if (bag is null)
                continue;
            var cap = session.Inv.ItemBagInfo.TryGetValue(bag.ItemEntry, out var bi) ? bi.ContainerSlots : 0;
            if (cap > 0)
                yield return (s, bag, cap);
        }
    }

    /// <summary>Ёмкость надетой сумки в bag-слоте (0 — нет сумки/не контейнер). M6.13.</summary>
    public static uint Capacity(WorldSession session, int bagSlot)
    {
        var bag = MainItemAt(session, bagSlot);
        return bag is not null && session.Inv.ItemBagInfo.TryGetValue(bag.ItemEntry, out var bi) ? bi.ContainerSlots : 0;
    }

    /// <summary>Есть ли предметы внутри сумки bag-слота (Bag == bagSlot). M6.13.</summary>
    public static bool HasItems(WorldSession session, int bagSlot)
        => session.Inv.Inventory.Any(i => i.Bag == (byte)bagSlot);

    /// <summary>Первый свободный внутренний слот сумки bag-слота, либо -1. M6.13.</summary>
    public static int FreeInnerSlot(WorldSession session, int bagSlot)
    {
        var cap = Capacity(session, bagSlot);
        if (cap == 0)
            return -1;
        var taken = session.Inv.Inventory.Where(i => i.Bag == (byte)bagSlot).Select(i => (int)i.Slot).ToHashSet();
        for (var s = 0; s < cap; s++)
        {
            if (!taken.Contains(s))
                return s;
        }

        return -1;
    }

    /// <summary>Первый свободный слот рюкзака (23..38) основного контейнера; -1 если места нет.
    /// (M7 S6: единый источник — раньше дублировался в InventoryHandlers/InventoryGrant.)</summary>
    public static int FreeBackpackSlot(WorldSession session)
    {
        var taken = session.Inv.Inventory.Where(i => i.Bag == InventorySlots.MainBag).Select(i => (int)i.Slot).ToHashSet();
        for (var s = InventorySlots.BackpackStart; s < InventorySlots.BackpackEnd; s++)
        {
            if (!taken.Contains(s))
                return s;
        }

        return -1;
    }

    /// <summary>Первое свободное место в контейнере <paramref name="bag"/> (255 — рюкзак, 19..22 — сумка). -1 — нет.</summary>
    public static int FreeSlotIn(WorldSession session, int bag)
        => bag == InventorySlots.MainBag ? FreeBackpackSlot(session) : FreeInnerSlot(session, bag);

    /// <summary>Первый свободный bag-слот 19..22 (нет надетой сумки), либо -1. M6.13.</summary>
    public static int FreeBagSlot(WorldSession session)
    {
        for (var s = InventorySlots.BagSlotStart; s < InventorySlots.BagSlotEnd; s++)
        {
            if (MainItemAt(session, s) is null)
                return s;
        }

        return -1;
    }

    /// <summary>Первый bag-слот с НАДЕТОЙ, но ПУСТОЙ сумкой (для свопа при автоэкипировке), либо -1. M6.13.</summary>
    public static int FirstEmptyEquippedBagSlot(WorldSession session)
    {
        for (var s = InventorySlots.BagSlotStart; s < InventorySlots.BagSlotEnd; s++)
        {
            if (MainItemAt(session, s) is not null && !HasItems(session, s))
                return s;
        }

        return -1;
    }

    /// <summary>Первое свободное место хранения: сперва рюкзак (23..38), затем надетые сумки. null — мест нет. M6.13.</summary>
    public static (int Bag, int Slot)? FirstFreeStoreSlot(WorldSession session)
    {
        var backpack = FreeBackpackSlot(session);
        if (backpack >= 0)
            return (InventorySlots.MainBag, backpack);
        foreach (var (bagSlot, _, _) in EquippedBags(session))
        {
            var inner = FreeInnerSlot(session, bagSlot);
            if (inner >= 0)
                return (bagSlot, inner);
        }
        return null;
    }

    /// <summary>Предмет по позиции (bag, slot): bag=255 — основной контейнер, 19..22 — внутри надетой сумки.</summary>
    public static InventoryItem? ItemAt(WorldSession session, int bag, int slot)
        => session.Inv.Inventory.FirstOrDefault(i => i.Bag == (byte)bag && i.Slot == (byte)slot);

    /// <summary>Предмет в основном контейнере по слоту (Bag==255). M6.13.</summary>
    public static InventoryItem? MainItemAt(WorldSession session, int slot)
        => session.Inv.Inventory.FirstOrDefault(i => i.Bag == InventorySlots.MainBag && i.Slot == slot);

    /// <summary>guid сумки (контейнера) в bag-слоте, либо 0. M6.13.</summary>
    public static ulong BagGuid(WorldSession session, int bagSlot)
    {
        var bag = MainItemAt(session, bagSlot);
        return bag is null ? 0UL : ItemObject.ItemGuid(bag.ItemGuid);
    }
}
