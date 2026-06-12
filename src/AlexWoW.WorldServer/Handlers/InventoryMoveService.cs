using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Операции инвентаря (M6.9 + сумки M6.13, DI-сервис M7 S6 — логика из InventoryHandlers):
/// перемещение/своп с валидацией экипировки/сумок, автоэкипировка, сплит стака, выброс. Позиция
/// предмета — пара (bag, slot), см. <see cref="BagInventory"/>. Сервер авторитетен: при отказе
/// пере-утверждаем текущее состояние через <see cref="InventoryClientSync"/>.
/// </summary>
internal sealed class InventoryMoveService(
    InventoryClientSync sync,
    ProgressionService progression,
    IInventoryRepository items,
    IWorldRepository worldDb)
{
    /// <summary>
    /// Перемещение/обмен предметов между позициями (bag,slot) с валидацией экипировки/сумок (M6.9 + M6.13).
    /// Позиции — основной контейнер (bag=255) или внутренность надетой сумки (bag=19..22).
    /// </summary>
    internal async Task MoveOrSwapAsync(WorldSession session, int srcBag, int srcSlot,
        int dstBag, int dstSlot, CancellationToken ct)
    {
        if (srcBag == dstBag && srcSlot == dstSlot) return;
        var src = BagInventory.ItemAt(session, srcBag, srcSlot);
        if (src is null)
        {
            // Источник пуст — освободить курсор (иначе клиент держит «поднятый» предмет серым).
            await sync.SendEquipErrorAsync(session, InventoryClientSync.EquipErrItemDoesntGoToSlot, 0, 0, ct);
            await sync.ReassertPosAsync(session, ct, (srcBag, srcSlot), (dstBag, dstSlot));
            return;
        }
        var dst = BagInventory.ItemAt(session, dstBag, dstSlot);

        var srcInv = await InvTypeAsync(src.ItemEntry, ct);
        var dstInv = dst is not null ? await InvTypeAsync(dst.ItemEntry, ct) : 0u;
        bool srcMain = srcBag == InventorySlots.MainBag, dstMain = dstBag == InventorySlots.MainBag;

        // Отказ: SMSG_INVENTORY_CHANGE_FAILURE (вернуть предмет с курсора — иначе он залипает серым до релога)
        // + пере-утвердить слоты. M6.13 (баг с залипанием сумки).
        async Task RejectAsync(byte err)
        {
            await sync.SendEquipErrorAsync(session, err,
                ItemObject.ItemGuid(src.ItemGuid), dst is null ? 0UL : ItemObject.ItemGuid(dst.ItemGuid), ct);
            await sync.ReassertPosAsync(session, ct, (srcBag, srcSlot), (dstBag, dstSlot));
        }

        // Экипировка (осн. контейнер): можно класть только подходящий тип.
        if (dstMain && InventorySlots.IsEquipmentSlot(dstSlot)
            && !InventorySlots.CanEquipInSlot(srcInv, dstSlot))
        { await RejectAsync(InventoryClientSync.EquipErrItemDoesntGoToSlot); return; }
        if (dst is not null && srcMain && InventorySlots.IsEquipmentSlot(srcSlot)
            && !InventorySlots.CanEquipInSlot(dstInv, srcSlot))
        { await RejectAsync(InventoryClientSync.EquipErrItemDoesntGoToSlot); return; }

        // bag-слот осн. контейнера (19..22): только сумка; непустую сумку нельзя снять/вытеснить.
        if (dstMain && InventorySlots.IsBagSlot(dstSlot) && srcInv != InventorySlots.InvTypeBag)
        { await RejectAsync(InventoryClientSync.EquipErrItemDoesntGoToSlot); return; }
        if (dst is not null && srcMain && InventorySlots.IsBagSlot(srcSlot) && dstInv != InventorySlots.InvTypeBag)
        { await RejectAsync(InventoryClientSync.EquipErrItemDoesntGoToSlot); return; }
        if ((srcMain && InventorySlots.IsBagSlot(srcSlot) && BagInventory.HasItems(session, srcSlot))
            || (dst is not null && dstMain && InventorySlots.IsBagSlot(dstSlot) && BagInventory.HasItems(session, dstSlot)))
        { await RejectAsync(InventoryClientSync.EquipErrOnlyWithEmptyBags); return; }

        // Внутрь сумки (dstBag 19..22): по ёмкости; без вложенности (сумку в сумку нельзя).
        if (!dstMain)
        {
            var cap = BagInventory.Capacity(session, dstBag);
            if (cap == 0 || dstSlot >= cap || srcInv == InventorySlots.InvTypeBag)
            { await RejectAsync(InventoryClientSync.EquipErrItemDoesntGoToSlot); return; }
        }
        if (dst is not null && !srcMain && dstInv == InventorySlots.InvTypeBag)
        { await RejectAsync(InventoryClientSync.EquipErrItemDoesntGoToSlot); return; }

        // --- применяем ---
        src.Bag = (byte)dstBag; src.Slot = (byte)dstSlot;
        await items.MoveItemAsync(src.ItemGuid, (byte)dstBag, (byte)dstSlot, ct);
        if (dst is not null)
        {
            dst.Bag = (byte)srcBag; dst.Slot = (byte)srcSlot;
            await items.MoveItemAsync(dst.ItemGuid, (byte)srcBag, (byte)srcSlot, ct);
        }

        await sync.ReassertPosAsync(session, ct, (srcBag, srcSlot), (dstBag, dstSlot));
        await sync.SendContainedAsync(session, src, ct);
        if (dst is not null) await sync.SendContainedAsync(session, dst, ct);

        // Смена экипировки → пересчёт боевых/защитных полей: гл. рука (урон/AP/парри), офф-хенд (щит→блок),
        // прочие слоты (броня предметов). Любой equip-слот мог измениться — обновляем по нему.
        if (InventorySlots.IsEquipmentSlot(srcSlot) && srcMain || InventorySlots.IsEquipmentSlot(dstSlot) && dstMain)
            await progression.RefreshMeleeAsync(session, ct);
    }

    /// <summary>Автоэкипировка по клику (CMSG_AUTO_EQUIP_ITEM): надетое — снять в свободное место;
    /// сумку — в bag-слот; прочее — в подходящий слот экипировки (кольцо/тринкет — альтернативный). M6.9/M6.13.</summary>
    internal async Task AutoEquipAsync(WorldSession session, int srcBag, int src, CancellationToken ct)
    {
        var item = BagInventory.ItemAt(session, srcBag, src);
        if (item is null) return;

        // Предмет уже надет (src — слот экипировки осн. контейнера) → СНЯТИЕ в первое свободное место.
        if (srcBag == InventorySlots.MainBag && InventorySlots.IsEquipmentSlot(src))
        {
            var free = BagInventory.FirstFreeStoreSlot(session);
            if (free is { } f) await MoveOrSwapAsync(session, srcBag, src, f.Bag, f.Slot, ct);
            else await sync.RejectEquipAsync(session, InventoryClientSync.EquipErrInventoryFull, item, ct, (srcBag, src));
            return;
        }

        var inv = await InvTypeAsync(item.ItemEntry, ct);

        // Сумка (INVTYPE_BAG) — надеть в первый свободный bag-слот 19..22 (или своп с пустой надетой). M6.13.
        if (inv == InventorySlots.InvTypeBag)
        {
            var bagSlot = BagInventory.FreeBagSlot(session);
            if (bagSlot < 0) bagSlot = BagInventory.FirstEmptyEquippedBagSlot(session);
            if (bagSlot < 0)
            { await sync.RejectEquipAsync(session, InventoryClientSync.EquipErrInventoryFull, item, ct, (srcBag, src)); return; }
            await MoveOrSwapAsync(session, srcBag, src, InventorySlots.MainBag, bagSlot, ct);
            return;
        }

        var slot = InventorySlots.EquipSlotFor(inv);
        if (slot < 0) // не экипируется
        { await sync.RejectEquipAsync(session, InventoryClientSync.EquipErrItemDoesntGoToSlot, item, ct, (srcBag, src)); return; }

        // кольцо/тринкет: если основной слот занят, а альтернативный свободен — берём альтернативный.
        if (BagInventory.MainItemAt(session, slot) is not null)
        {
            if (inv == 11 && BagInventory.MainItemAt(session, 11) is null) slot = 11;
            else if (inv == 12 && BagInventory.MainItemAt(session, 13) is null) slot = 13;
        }
        await MoveOrSwapAsync(session, srcBag, src, InventorySlots.MainBag, slot, ct);
    }

    /// <summary>Сплит стака (CMSG_SPLIT_ITEM): отделяет <paramref name="amount"/> в пустой слот хранения
    /// (рюкзак или внутренность сумки) — новый предмет с персистом и create-апдейтом клиенту. M6.9/M6.13.</summary>
    internal async Task SplitAsync(WorldSession session, int srcBag, int src, int dstBag, int dst,
        uint amount, CancellationToken ct)
    {
        var item = BagInventory.ItemAt(session, srcBag, src);
        if (item is null) { await sync.ReassertPosAsync(session, ct, (srcBag, src), (dstBag, dst)); return; }
        if (amount == 0 || amount >= item.StackCount)
        { await sync.ReassertPosAsync(session, ct, (srcBag, src), (dstBag, dst)); return; }
        // dst — пустой слот хранения: рюкзак (осн.) или внутренность сумки (по ёмкости).
        var dstOk = dstBag == InventorySlots.MainBag
            ? InventorySlots.IsBackpackSlot(dst)
            : dst < BagInventory.Capacity(session, dstBag);
        if (!dstOk || BagInventory.ItemAt(session, dstBag, dst) is not null)
        { await sync.ReassertPosAsync(session, ct, (srcBag, src), (dstBag, dst)); return; }

        var owner = session.InWorldGuid;
        item.StackCount -= amount;
        await items.SetItemStackAsync(item.ItemGuid, item.StackCount, ct);

        var newLow = await items.AddItemAsync(owner, item.ItemEntry, (byte)dstBag, (byte)dst, amount, ct);
        var newItem = new InventoryItem
        {
            ItemGuid = newLow,
            OwnerGuid = owner,
            ItemEntry = item.ItemEntry,
            Bag = (byte)dstBag,
            Slot = (byte)dst,
            StackCount = amount,
        };
        session.Inv.Inventory.Add(newItem);

        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            ItemObject.BuildStackUpdate(ItemObject.ItemGuid(item.ItemGuid), item.StackCount), ct);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            ItemObject.BuildItemsCreate(new[] { newItem }, owner, session.Inv.ItemBagInfo), ct);
        await sync.ReassertPosAsync(session, ct, (dstBag, dst));
        await sync.SendContainedAsync(session, newItem, ct);
        session.Logger.LogDebug("SPLIT '{User}': {Entry} {Amt} → bag={DB} slot={Dst}", session.Account, item.ItemEntry, amount, dstBag, dst);
    }

    /// <summary>Выброс предмета (CMSG_DESTROY_ITEM): стопку целиком — удалить (DestroyObject + очистка слота),
    /// часть — уменьшить стак. Непустую надетую сумку уничтожить нельзя. M6.9/M6.13.</summary>
    internal async Task DestroyAsync(WorldSession session, int bag, int slot, uint amount, CancellationToken ct)
    {
        var item = BagInventory.ItemAt(session, bag, slot);
        if (item is null) return;

        // Нельзя уничтожить непустую надетую сумку.
        if (bag == InventorySlots.MainBag && InventorySlots.IsBagSlot(slot) && BagInventory.HasItems(session, slot))
        { await sync.RejectEquipAsync(session, InventoryClientSync.EquipErrOnlyWithEmptyBags, item, ct, (bag, slot)); return; }

        if (amount == 0 || amount >= item.StackCount)
        {
            await items.RemoveItemAsync(item.ItemGuid, ct);
            session.Inv.Inventory.Remove(item);
            await session.SendAsync(WorldOpcode.SmsgDestroyObject,
                new ByteWriter(9).UInt64(ItemObject.ItemGuid(item.ItemGuid)).UInt8(0).ToArray(), ct);
            await sync.ReassertPosAsync(session, ct, (bag, slot));
        }
        else
        {
            item.StackCount -= amount;
            await items.SetItemStackAsync(item.ItemGuid, item.StackCount, ct);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                ItemObject.BuildStackUpdate(ItemObject.ItemGuid(item.ItemGuid), item.StackCount), ct);
        }
        session.Logger.LogDebug("DESTROY '{User}': слот {Slot} x{Amt}", session.Account, slot, amount);
    }

    // Не static (M7 S9): репозиторий мира приходит ctor-инъекцией, а не через сессию.
    private async Task<uint> InvTypeAsync(uint entry, CancellationToken ct)
    {
        try
        {
            var d = await worldDb.GetItemDisplaysAsync(new[] { entry }, ct);
            return d.TryGetValue(entry, out var v) ? v.InventoryType : 0u;
        }
        catch { return 0u; }
    }
}
