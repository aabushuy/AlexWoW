using AlexWoW.Common.Network;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Управление инвентарём (M6.9): перемещение/своп слотов, сплит стака, экипировка кликом, выброс.
/// Работаем в основном контейнере (bag = INVENTORY_SLOT_BAG_0 = 255): экипировка 0..18, рюкзак 23..38.
/// Сервер авторитетен: клиент ждёт обновления полей слотов; при отказе пере-утверждаем текущее
/// состояние (предмет «возвращается» на место).
/// </summary>
public static class InventoryHandlers
{
    [WorldOpcodeHandler(WorldOpcode.CmsgSwapInvItem)]
    public static async Task OnSwapInvItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        // ВНИМАНИЕ: порядок байт — destination_slot, ПОТОМ source_slot (как CMaNGOS
        // `recv_data >> dstslot >> srcslot`). При свопе двух занятых слотов это незаметно
        // (симметрично), но при перемещении/снятии в ПУСТОЙ слот порядок критичен — иначе
        // источник=пустой слот → операция игнорируется (M7 #18: не снималась экипировка).
        var r = packet.Reader();
        int dst = r.UInt8(), src = r.UInt8();
        session.Logger.LogDebug("[inv] SWAP_INV src={Src} dst={Dst}", src, dst);
        await MoveOrSwapAsync(session, src, dst, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgSwapItem)]
    public static async Task OnSwapItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int dstBag = r.UInt8(), dst = r.UInt8(), srcBag = r.UInt8(), src = r.UInt8();
        session.Logger.LogDebug("[inv] SWAP_ITEM srcBag={SB} src={S} dstBag={DB} dst={D}", srcBag, src, dstBag, dst);
        if (!IsMain(srcBag) || !IsMain(dstBag)) return; // доп. сумки-контейнеры пока не поддержаны
        await MoveOrSwapAsync(session, src, dst, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAutostoreBagItem)]
    public static async Task OnAutostoreBagItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int srcBag = r.UInt8(), src = r.UInt8(), dstBag = r.UInt8();
        session.Logger.LogDebug("[inv] AUTOSTORE srcBag={SB} src={S} dstBag={DB}", srcBag, src, dstBag);
        if (!IsMain(srcBag) || !IsMain(dstBag)) return;
        var free = FreeBackpackSlot(session);
        if (free >= 0) await MoveOrSwapAsync(session, src, free, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAutoEquipItem)]
    public static async Task OnAutoEquipItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int srcBag = r.UInt8(), src = r.UInt8();
        session.Logger.LogDebug("[inv] AUTOEQUIP srcBag={SB} src={S}", srcBag, src);
        if (!IsMain(srcBag)) return;

        var item = ItemAt(session, src);
        if (item is null) return;

        // Предмет уже надет (src — слот экипировки) → это СНЯТИЕ: в первый свободный слот рюкзака.
        if (InventorySlots.IsEquipmentSlot(src))
        {
            var freeSlot = FreeBackpackSlot(session);
            if (freeSlot >= 0) await MoveOrSwapAsync(session, src, freeSlot, ct);
            else await ReassertAsync(session, ct, src);
            return;
        }

        var inv = await InvTypeAsync(session, item.ItemEntry, ct);
        var slot = InventorySlots.EquipSlotFor(inv);
        if (slot < 0) { await ReassertAsync(session, ct, src); return; } // не экипируется

        // кольцо/тринкет: если основной слот занят, а альтернативный свободен — берём альтернативный.
        if (ItemAt(session, slot) is not null)
        {
            if (inv == 11 && ItemAt(session, 11) is null) slot = 11;
            else if (inv == 12 && ItemAt(session, 13) is null) slot = 13;
        }
        await MoveOrSwapAsync(session, src, slot, ct);
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
        await MoveOrSwapAsync(session, item.Slot, dst, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgSplitItem)]
    public static async Task OnSplitItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int srcBag = r.UInt8(), src = r.UInt8(), dstBag = r.UInt8(), dst = r.UInt8();
        var amount = r.UInt32();
        if (!IsMain(srcBag) || !IsMain(dstBag)) return;

        var item = ItemAt(session, src);
        if (item is null) { await ReassertAsync(session, ct, src, dst); return; }
        if (amount == 0 || amount >= item.StackCount) { await ReassertAsync(session, ct, src, dst); return; }
        if (!InventorySlots.IsBackpackSlot(dst) || ItemAt(session, dst) is not null)
        { await ReassertAsync(session, ct, src, dst); return; } // только в пустой слот рюкзака

        var owner = session.InWorldGuid;
        item.StackCount -= amount;
        await session.Characters.SetItemStackAsync(item.ItemGuid, item.StackCount, ct);

        var newLow = await session.Characters.AddItemAsync(owner, item.ItemEntry,
            InventorySlots.MainBag, (byte)dst, amount, ct);
        var newItem = new InventoryItem
        {
            ItemGuid = newLow, OwnerGuid = owner, ItemEntry = item.ItemEntry,
            Bag = InventorySlots.MainBag, Slot = (byte)dst, StackCount = amount,
        };
        session.Inventory.Add(newItem);

        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            ItemObject.BuildStackUpdate(ItemObject.ItemGuid(item.ItemGuid), item.StackCount), ct);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            ItemObject.BuildItemsCreate(new[] { newItem }, owner), ct);
        await ReassertAsync(session, ct, dst);
        session.Logger.LogDebug("SPLIT '{User}': {Entry} {Amt} в слот {Dst}", session.Account, item.ItemEntry, amount, dst);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgDestroyItem)]
    public static async Task OnDestroyItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int bag = r.UInt8(), slot = r.UInt8();
        var amount = r.UInt8();
        if (!IsMain(bag)) return;

        var item = ItemAt(session, slot);
        if (item is null) return;

        if (amount == 0 || amount >= item.StackCount)
        {
            await session.Characters.RemoveItemAsync(item.ItemGuid, ct);
            session.Inventory.Remove(item);
            await session.SendAsync(WorldOpcode.SmsgDestroyObject,
                new ByteWriter(9).UInt64(ItemObject.ItemGuid(item.ItemGuid)).UInt8(0).ToArray(), ct);
            await ReassertAsync(session, ct, slot);
        }
        else
        {
            item.StackCount -= amount;
            await session.Characters.SetItemStackAsync(item.ItemGuid, item.StackCount, ct);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                ItemObject.BuildStackUpdate(ItemObject.ItemGuid(item.ItemGuid), item.StackCount), ct);
        }
        session.Logger.LogDebug("DESTROY '{User}': слот {Slot} x{Amt}", session.Account, slot, amount);
    }

    /// <summary>Перемещение/обмен двух слотов основного контейнера (с валидацией экипировки).</summary>
    private static async Task MoveOrSwapAsync(WorldSession session, int srcSlot, int dstSlot, CancellationToken ct)
    {
        if (srcSlot == dstSlot) return;
        var src = ItemAt(session, srcSlot);
        if (src is null) { await ReassertAsync(session, ct, srcSlot, dstSlot); return; }
        var dst = ItemAt(session, dstSlot);

        // Нельзя положить неэкипируемое/неподходящее в слот экипировки.
        if (InventorySlots.IsEquipmentSlot(dstSlot)
            && !InventorySlots.CanEquipInSlot(await InvTypeAsync(session, src.ItemEntry, ct), dstSlot))
        { await ReassertAsync(session, ct, srcSlot, dstSlot); return; }
        if (dst is not null && InventorySlots.IsEquipmentSlot(srcSlot)
            && !InventorySlots.CanEquipInSlot(await InvTypeAsync(session, dst.ItemEntry, ct), srcSlot))
        { await ReassertAsync(session, ct, srcSlot, dstSlot); return; }

        src.Slot = (byte)dstSlot;
        await session.Characters.MoveItemAsync(src.ItemGuid, InventorySlots.MainBag, (byte)dstSlot, ct);
        if (dst is not null)
        {
            dst.Slot = (byte)srcSlot;
            await session.Characters.MoveItemAsync(dst.ItemGuid, InventorySlots.MainBag, (byte)srcSlot, ct);
        }
        await ReassertAsync(session, ct, srcSlot, dstSlot);

        // M7 #16: смена оружия гл. руки → пересчёт урона/скорости/attack-power (иначе нужен релог).
        if (srcSlot == InventorySlots.MainHandSlot || dstSlot == InventorySlots.MainHandSlot)
            await Progression.RefreshMeleeAsync(session, ct);
    }

    /// <summary>Отправляет клиенту текущее состояние указанных слотов (guid + видимый предмет для экипировки).</summary>
    private static async Task ReassertAsync(WorldSession session, CancellationToken ct, params int[] slots)
    {
        var guid = (ulong)session.InWorldGuid;
        var pkt = PlayerSpawn.BuildPlayerValuesUpdate(guid, m =>
        {
            foreach (var slot in slots)
            {
                var it = ItemAt(session, slot);
                m.SetUInt64(UpdateField.InvSlotGuid(slot), it is null ? 0UL : ItemObject.ItemGuid(it.ItemGuid));
                if (InventorySlots.IsEquipmentSlot(slot))
                    m.SetUInt32(UpdateField.VisibleItemEntry(slot), it?.ItemEntry ?? 0u);
            }
        });
        await session.SendAsync(WorldOpcode.SmsgUpdateObject, pkt, ct);
    }

    private static InventoryItem? ItemAt(WorldSession session, int slot)
        => session.Inventory.FirstOrDefault(i => i.Bag == InventorySlots.MainBag && i.Slot == slot);

    private static async Task<uint> InvTypeAsync(WorldSession session, uint entry, CancellationToken ct)
    {
        try
        {
            var d = await session.WorldDb.GetItemDisplaysAsync(new[] { entry }, ct);
            return d.TryGetValue(entry, out var v) ? v.InventoryType : 0u;
        }
        catch { return 0u; }
    }

    private static int FreeBackpackSlot(WorldSession session)
    {
        var taken = session.Inventory.Where(i => i.Bag == InventorySlots.MainBag).Select(i => (int)i.Slot).ToHashSet();
        for (var s = InventorySlots.BackpackStart; s < InventorySlots.BackpackEnd; s++)
            if (!taken.Contains(s)) return s;
        return -1;
    }

    private static bool IsMain(int bag) => bag == InventorySlots.MainBag;
}
