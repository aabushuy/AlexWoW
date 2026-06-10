using AlexWoW.Common.Network;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Управление инвентарём (M6.9 + сумки M6.13): перемещение/своп слотов, сплит стака, экипировка кликом,
/// выброс. Позиция предмета — пара (bag, slot): bag=255 — основной контейнер (экипировка 0..18, bag-слоты
/// 19..22, рюкзак 23..38); bag=19..22 — ВНУТРИ надетой сумки (slot = индекс 0..ContainerSlots-1). Сервер
/// авторитетен: при отказе пере-утверждаем текущее состояние (предмет «возвращается» на место).
/// </summary>
public static class InventoryHandlers
{
    [WorldOpcodeHandler(WorldOpcode.CmsgSwapInvItem)]
    public static async Task OnSwapInvItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        // Порядок байт — destination_slot, ПОТОМ source_slot (CMaNGOS `recv_data >> dstslot >> srcslot`).
        // Оба слота — в основном контейнере (bag=255). M7 #18.
        var r = packet.Reader();
        int dst = r.UInt8(), src = r.UInt8();
        session.Logger.LogDebug("[inv] SWAP_INV src={Src} dst={Dst}", src, dst);
        await MoveOrSwapAsync(session, InventorySlots.MainBag, src, InventorySlots.MainBag, dst, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgSwapItem)]
    public static async Task OnSwapItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int dstBag = r.UInt8(), dst = r.UInt8(), srcBag = r.UInt8(), src = r.UInt8();
        session.Logger.LogDebug("[inv] SWAP_ITEM srcBag={SB} src={S} dstBag={DB} dst={D}", srcBag, src, dstBag, dst);
        if (!ValidContainer(session, srcBag) || !ValidContainer(session, dstBag)) return;
        await MoveOrSwapAsync(session, srcBag, src, dstBag, dst, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAutostoreBagItem)]
    public static async Task OnAutostoreBagItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int srcBag = r.UInt8(), src = r.UInt8(), dstBag = r.UInt8();
        session.Logger.LogDebug("[inv] AUTOSTORE srcBag={SB} src={S} dstBag={DB}", srcBag, src, dstBag);
        if (!ValidContainer(session, srcBag) || !ValidContainer(session, dstBag)) return;
        var free = FreeSlotIn(session, dstBag);
        if (free >= 0) await MoveOrSwapAsync(session, srcBag, src, dstBag, free, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAutoEquipItem)]
    public static async Task OnAutoEquipItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int srcBag = r.UInt8(), src = r.UInt8();
        session.Logger.LogDebug("[inv] AUTOEQUIP srcBag={SB} src={S}", srcBag, src);
        if (!ValidContainer(session, srcBag)) return;

        var item = ItemAt(session, srcBag, src);
        if (item is null) return;

        // Предмет уже надет (src — слот экипировки осн. контейнера) → СНЯТИЕ в первое свободное место.
        if (srcBag == InventorySlots.MainBag && InventorySlots.IsEquipmentSlot(src))
        {
            var free = BagInventory.FirstFreeStoreSlot(session);
            if (free is { } f) await MoveOrSwapAsync(session, srcBag, src, f.Bag, f.Slot, ct);
            else await RejectEquipAsync(session, EquipErrInventoryFull, item, ct, (srcBag, src));
            return;
        }

        var inv = await InvTypeAsync(session, item.ItemEntry, ct);

        // Сумка (INVTYPE_BAG) — надеть в первый свободный bag-слот 19..22 (или своп с пустой надетой). M6.13.
        if (inv == InventorySlots.InvTypeBag)
        {
            var bagSlot = FreeBagSlot(session);
            if (bagSlot < 0) bagSlot = FirstEmptyEquippedBagSlot(session);
            if (bagSlot < 0) { await RejectEquipAsync(session, EquipErrInventoryFull, item, ct, (srcBag, src)); return; }
            await MoveOrSwapAsync(session, srcBag, src, InventorySlots.MainBag, bagSlot, ct);
            return;
        }

        var slot = InventorySlots.EquipSlotFor(inv);
        if (slot < 0) { await RejectEquipAsync(session, EquipErrItemDoesntGoToSlot, item, ct, (srcBag, src)); return; } // не экипируется

        // кольцо/тринкет: если основной слот занят, а альтернативный свободен — берём альтернативный.
        if (ItemAt(session, slot) is not null)
        {
            if (inv == 11 && ItemAt(session, 11) is null) slot = 11;
            else if (inv == 12 && ItemAt(session, 13) is null) slot = 13;
        }
        await MoveOrSwapAsync(session, srcBag, src, InventorySlots.MainBag, slot, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAutoEquipItemSlot)]
    public static async Task OnAutoEquipItemSlot(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var itemGuid = r.UInt64();
        int dst = r.UInt8();
        var low = (uint)(itemGuid & 0xFFFFFFFF);
        var item = session.Inventory.FirstOrDefault(i => i.ItemGuid == low);
        if (item is null) return;
        await MoveOrSwapAsync(session, item.Bag, item.Slot, InventorySlots.MainBag, dst, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgSplitItem)]
    public static async Task OnSplitItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int srcBag = r.UInt8(), src = r.UInt8(), dstBag = r.UInt8(), dst = r.UInt8();
        var amount = r.UInt32();
        if (!ValidContainer(session, srcBag) || !ValidContainer(session, dstBag)) return;

        var item = ItemAt(session, srcBag, src);
        if (item is null) { await ReassertPosAsync(session, ct, (srcBag, src), (dstBag, dst)); return; }
        if (amount == 0 || amount >= item.StackCount) { await ReassertPosAsync(session, ct, (srcBag, src), (dstBag, dst)); return; }
        // dst — пустой слот хранения: рюкзак (осн.) или внутренность сумки (по ёмкости).
        var dstOk = dstBag == InventorySlots.MainBag
            ? InventorySlots.IsBackpackSlot(dst)
            : dst < BagInventory.Capacity(session, dstBag);
        if (!dstOk || ItemAt(session, dstBag, dst) is not null)
        { await ReassertPosAsync(session, ct, (srcBag, src), (dstBag, dst)); return; }

        var owner = session.InWorldGuid;
        item.StackCount -= amount;
        await session.Items.SetItemStackAsync(item.ItemGuid, item.StackCount, ct);

        var newLow = await session.Items.AddItemAsync(owner, item.ItemEntry, (byte)dstBag, (byte)dst, amount, ct);
        var newItem = new InventoryItem
        {
            ItemGuid = newLow, OwnerGuid = owner, ItemEntry = item.ItemEntry,
            Bag = (byte)dstBag, Slot = (byte)dst, StackCount = amount,
        };
        session.Inventory.Add(newItem);

        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            ItemObject.BuildStackUpdate(ItemObject.ItemGuid(item.ItemGuid), item.StackCount), ct);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            ItemObject.BuildItemsCreate(new[] { newItem }, owner, session.ItemBagInfo), ct);
        await ReassertPosAsync(session, ct, (dstBag, dst));
        await SendContainedAsync(session, newItem, ct);
        session.Logger.LogDebug("SPLIT '{User}': {Entry} {Amt} → bag={DB} slot={Dst}", session.Account, item.ItemEntry, amount, dstBag, dst);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgDestroyItem)]
    public static async Task OnDestroyItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int bag = r.UInt8(), slot = r.UInt8();
        var amount = r.UInt8();
        if (!ValidContainer(session, bag)) return;

        var item = ItemAt(session, bag, slot);
        if (item is null) return;

        // Нельзя уничтожить непустую надетую сумку.
        if (bag == InventorySlots.MainBag && InventorySlots.IsBagSlot(slot) && BagInventory.HasItems(session, slot))
        { await RejectEquipAsync(session, EquipErrOnlyWithEmptyBags, item, ct, (bag, slot)); return; }

        if (amount == 0 || amount >= item.StackCount)
        {
            await session.Items.RemoveItemAsync(item.ItemGuid, ct);
            session.Inventory.Remove(item);
            await session.SendAsync(WorldOpcode.SmsgDestroyObject,
                new ByteWriter(9).UInt64(ItemObject.ItemGuid(item.ItemGuid)).UInt8(0).ToArray(), ct);
            await ReassertPosAsync(session, ct, (bag, slot));
        }
        else
        {
            item.StackCount -= amount;
            await session.Items.SetItemStackAsync(item.ItemGuid, item.StackCount, ct);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                ItemObject.BuildStackUpdate(ItemObject.ItemGuid(item.ItemGuid), item.StackCount), ct);
        }
        session.Logger.LogDebug("DESTROY '{User}': слот {Slot} x{Amt}", session.Account, slot, amount);
    }

    /// <summary>
    /// Перемещение/обмен предметов между позициями (bag,slot) с валидацией экипировки/сумок (M6.9 + M6.13).
    /// Позиции — основной контейнер (bag=255) или внутренность надетой сумки (bag=19..22).
    /// </summary>
    private static async Task MoveOrSwapAsync(WorldSession session, int srcBag, int srcSlot,
        int dstBag, int dstSlot, CancellationToken ct)
    {
        if (srcBag == dstBag && srcSlot == dstSlot) return;
        var src = ItemAt(session, srcBag, srcSlot);
        if (src is null)
        {
            // Источник пуст — освободить курсор (иначе клиент держит «поднятый» предмет серым).
            await SendEquipErrorAsync(session, EquipErrItemDoesntGoToSlot, 0, 0, ct);
            await ReassertPosAsync(session, ct, (srcBag, srcSlot), (dstBag, dstSlot));
            return;
        }
        var dst = ItemAt(session, dstBag, dstSlot);

        var srcInv = await InvTypeAsync(session, src.ItemEntry, ct);
        var dstInv = dst is not null ? await InvTypeAsync(session, dst.ItemEntry, ct) : 0u;
        bool srcMain = srcBag == InventorySlots.MainBag, dstMain = dstBag == InventorySlots.MainBag;

        // Отказ: SMSG_INVENTORY_CHANGE_FAILURE (вернуть предмет с курсора — иначе он залипает серым до релога)
        // + пере-утвердить слоты. M6.13 (баг с залипанием сумки).
        async Task RejectAsync(byte err)
        {
            await SendEquipErrorAsync(session, err,
                ItemObject.ItemGuid(src.ItemGuid), dst is null ? 0UL : ItemObject.ItemGuid(dst.ItemGuid), ct);
            await ReassertPosAsync(session, ct, (srcBag, srcSlot), (dstBag, dstSlot));
        }

        // Экипировка (осн. контейнер): можно класть только подходящий тип.
        if (dstMain && InventorySlots.IsEquipmentSlot(dstSlot)
            && !InventorySlots.CanEquipInSlot(srcInv, dstSlot))
        { await RejectAsync(EquipErrItemDoesntGoToSlot); return; }
        if (dst is not null && srcMain && InventorySlots.IsEquipmentSlot(srcSlot)
            && !InventorySlots.CanEquipInSlot(dstInv, srcSlot))
        { await RejectAsync(EquipErrItemDoesntGoToSlot); return; }

        // bag-слот осн. контейнера (19..22): только сумка; непустую сумку нельзя снять/вытеснить.
        if (dstMain && InventorySlots.IsBagSlot(dstSlot) && srcInv != InventorySlots.InvTypeBag)
        { await RejectAsync(EquipErrItemDoesntGoToSlot); return; }
        if (dst is not null && srcMain && InventorySlots.IsBagSlot(srcSlot) && dstInv != InventorySlots.InvTypeBag)
        { await RejectAsync(EquipErrItemDoesntGoToSlot); return; }
        if ((srcMain && InventorySlots.IsBagSlot(srcSlot) && BagInventory.HasItems(session, srcSlot))
            || (dst is not null && dstMain && InventorySlots.IsBagSlot(dstSlot) && BagInventory.HasItems(session, dstSlot)))
        { await RejectAsync(EquipErrOnlyWithEmptyBags); return; }

        // Внутрь сумки (dstBag 19..22): по ёмкости; без вложенности (сумку в сумку нельзя).
        if (!dstMain)
        {
            var cap = BagInventory.Capacity(session, dstBag);
            if (cap == 0 || dstSlot >= cap || srcInv == InventorySlots.InvTypeBag)
            { await RejectAsync(EquipErrItemDoesntGoToSlot); return; }
        }
        if (dst is not null && !srcMain && dstInv == InventorySlots.InvTypeBag)
        { await RejectAsync(EquipErrItemDoesntGoToSlot); return; }

        // --- применяем ---
        src.Bag = (byte)dstBag; src.Slot = (byte)dstSlot;
        await session.Items.MoveItemAsync(src.ItemGuid, (byte)dstBag, (byte)dstSlot, ct);
        if (dst is not null)
        {
            dst.Bag = (byte)srcBag; dst.Slot = (byte)srcSlot;
            await session.Items.MoveItemAsync(dst.ItemGuid, (byte)srcBag, (byte)srcSlot, ct);
        }

        await ReassertPosAsync(session, ct, (srcBag, srcSlot), (dstBag, dstSlot));
        await SendContainedAsync(session, src, ct);
        if (dst is not null) await SendContainedAsync(session, dst, ct);

        // M7 #16: смена оружия гл. руки → пересчёт урона/скорости/attack-power.
        if ((srcMain && srcSlot == InventorySlots.MainHandSlot) || (dstMain && dstSlot == InventorySlots.MainHandSlot))
            await Progression.RefreshMeleeAsync(session, ct);
    }

    /// <summary>Отправляет клиенту текущее состояние позиций: осн. слоты — через поле игрока (guid + видимый
    /// предмет для экипировки); внутренности сумок — через CONTAINER_FIELD_SLOT_x соответствующей сумки. M6.13.</summary>
    private static async Task ReassertPosAsync(WorldSession session, CancellationToken ct, params (int Bag, int Slot)[] positions)
    {
        var mainSlots = positions.Where(p => p.Bag == InventorySlots.MainBag).Select(p => p.Slot).Distinct().ToArray();
        if (mainSlots.Length > 0)
        {
            var guid = (ulong)session.InWorldGuid;
            var pkt = PlayerSpawn.BuildPlayerValuesUpdate(guid, m =>
            {
                foreach (var slot in mainSlots)
                {
                    var it = ItemAt(session, slot);
                    m.SetUInt64(UpdateField.InvSlotGuid(slot), it is null ? 0UL : ItemObject.ItemGuid(it.ItemGuid));
                    if (InventorySlots.IsEquipmentSlot(slot))
                        m.SetUInt32(UpdateField.VisibleItemEntry(slot), it?.ItemEntry ?? 0u);
                }
            });
            await session.SendAsync(WorldOpcode.SmsgUpdateObject, pkt, ct);
        }

        foreach (var (bag, slot) in positions.Where(p => InventorySlots.IsBagSlot(p.Bag)))
        {
            var bagGuid = BagInventory.BagGuid(session, bag);
            if (bagGuid == 0) continue;
            var it = ItemAt(session, bag, slot);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                ContainerObject.BuildSlotUpdate(bagGuid, slot, it is null ? 0UL : ItemObject.ItemGuid(it.ItemGuid)), ct);
        }
    }

    /// <summary>Главный контейнер: пере-утвердить осн. слоты (обёртка над <see cref="ReassertPosAsync"/>).</summary>
    private static Task ReassertAsync(WorldSession session, CancellationToken ct, params int[] slots)
        => ReassertPosAsync(session, ct, slots.Select(s => ((int)InventorySlots.MainBag, s)).ToArray());

    /// <summary>Обновляет ITEM_FIELD_CONTAINED предмета: внутри сумки — guid сумки, иначе — guid игрока. M6.13.</summary>
    private static Task SendContainedAsync(WorldSession session, InventoryItem item, CancellationToken ct)
    {
        var container = (ulong)session.InWorldGuid;
        if (InventorySlots.IsBagSlot(item.Bag))
        {
            var bagGuid = BagInventory.BagGuid(session, item.Bag);
            if (bagGuid != 0) container = bagGuid;
        }
        return session.SendAsync(WorldOpcode.SmsgUpdateObject,
            ItemObject.BuildContainedUpdate(ItemObject.ItemGuid(item.ItemGuid), container), ct);
    }

    // InventoryResult (SMSG_INVENTORY_CHANGE_FAILURE) — сверено с CMaNGOS Item.h.
    private const byte EquipErrItemDoesntGoToSlot = 3;
    private const byte EquipErrOnlyWithEmptyBags = 31;   // непустую сумку нельзя снять/уничтожить
    private const byte EquipErrInventoryFull = 50;

    /// <summary>
    /// SMSG_INVENTORY_CHANGE_FAILURE (3.3.5): отклоняет операцию инвентаря и ВОЗВРАЩАЕТ предмет с курсора.
    /// Без него клиент держит «поднятый» предмет (серый в слоте) до релога — отсюда залипание сумки. M6.13.
    /// Формат: u8 result; если result≠OK — u64 item1, u64 item2, u8 bag_subclass(0). (Level-поле только для
    /// CANT_EQUIP_LEVEL_I=1, нам не нужно.)
    /// </summary>
    private static Task SendEquipErrorAsync(WorldSession session, byte result, ulong item1, ulong item2, CancellationToken ct)
    {
        var w = new ByteWriter(20).UInt8(result);
        if (result != 0)
            w.UInt64(item1).UInt64(item2).UInt8(0);
        return session.SendAsync(WorldOpcode.SmsgInventoryChangeFailure, w.ToArray(), ct);
    }

    /// <summary>Отказ авто-операции: вернуть предмет с курсора (SMSG_INVENTORY_CHANGE_FAILURE) + пере-утвердить. M6.13.</summary>
    private static async Task RejectEquipAsync(WorldSession session, byte err, InventoryItem item,
        CancellationToken ct, params (int Bag, int Slot)[] positions)
    {
        await SendEquipErrorAsync(session, err, ItemObject.ItemGuid(item.ItemGuid), 0, ct);
        await ReassertPosAsync(session, ct, positions);
    }

    private static InventoryItem? ItemAt(WorldSession session, int bag, int slot)
        => session.Inventory.FirstOrDefault(i => i.Bag == (byte)bag && i.Slot == (byte)slot);

    /// <summary>Предмет осн. контейнера по слоту (Bag==255).</summary>
    private static InventoryItem? ItemAt(WorldSession session, int slot)
        => ItemAt(session, InventorySlots.MainBag, slot);

    private static async Task<uint> InvTypeAsync(WorldSession session, uint entry, CancellationToken ct)
    {
        try
        {
            var d = await session.WorldDb.GetItemDisplaysAsync(new[] { entry }, ct);
            return d.TryGetValue(entry, out var v) ? v.InventoryType : 0u;
        }
        catch { return 0u; }
    }

    /// <summary>Первое свободное место в контейнере <paramref name="bag"/> (255 — рюкзак, 19..22 — сумка). -1 — нет.</summary>
    private static int FreeSlotIn(WorldSession session, int bag)
        => bag == InventorySlots.MainBag ? FreeBackpackSlot(session) : BagInventory.FreeInnerSlot(session, bag);

    private static int FreeBackpackSlot(WorldSession session)
    {
        var taken = session.Inventory.Where(i => i.Bag == InventorySlots.MainBag).Select(i => (int)i.Slot).ToHashSet();
        for (var s = InventorySlots.BackpackStart; s < InventorySlots.BackpackEnd; s++)
            if (!taken.Contains(s)) return s;
        return -1;
    }

    /// <summary>Первый свободный bag-слот 19..22 (нет надетой сумки), либо -1. M6.13.</summary>
    private static int FreeBagSlot(WorldSession session)
    {
        for (var s = InventorySlots.BagSlotStart; s < InventorySlots.BagSlotEnd; s++)
            if (ItemAt(session, s) is null) return s;
        return -1;
    }

    /// <summary>Первый bag-слот с НАДЕТОЙ, но ПУСТОЙ сумкой (для свопа при автоэкипировке), либо -1. M6.13.</summary>
    private static int FirstEmptyEquippedBagSlot(WorldSession session)
    {
        for (var s = InventorySlots.BagSlotStart; s < InventorySlots.BagSlotEnd; s++)
            if (ItemAt(session, s) is not null && !BagInventory.HasItems(session, s)) return s;
        return -1;
    }

    /// <summary>Допустимый контейнер для адресации: осн. (255) или существующая надетая сумка (19..22). M6.13.</summary>
    private static bool ValidContainer(WorldSession session, int bag)
        => bag == InventorySlots.MainBag
           || (InventorySlots.IsBagSlot(bag) && BagInventory.MainItemAt(session, bag) is not null);
}
