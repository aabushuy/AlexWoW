using AlexWoW.Common.Network;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Сервер-авторитетная ресинхронизация инвентаря клиенту (M6.9/M6.13, DI-сервис M7 S6 — вынос из
/// InventoryHandlers): пере-утверждение позиций после операции/отказа, ITEM_FIELD_CONTAINED и
/// отказ операции (SMSG_INVENTORY_CHANGE_FAILURE). Используется <see cref="InventoryMoveService"/>.
/// </summary>
internal sealed class InventoryClientSync
{
    // InventoryResult (SMSG_INVENTORY_CHANGE_FAILURE) — сверено с CMaNGOS Item.h.
    internal const byte EquipErrItemDoesntGoToSlot = 3;
    internal const byte EquipErrOnlyWithEmptyBags = 31;   // непустую сумку нельзя снять/уничтожить
    internal const byte EquipErrInventoryFull = 50;

    /// <summary>Отправляет клиенту текущее состояние позиций: осн. слоты — через поле игрока (guid + видимый
    /// предмет для экипировки); внутренности сумок — через CONTAINER_FIELD_SLOT_x соответствующей сумки. M6.13.</summary>
    internal async Task ReassertPosAsync(WorldSession session, CancellationToken ct, params (int Bag, int Slot)[] positions)
    {
        var mainSlots = positions.Where(p => p.Bag == InventorySlots.MainBag).Select(p => p.Slot).Distinct().ToArray();
        if (mainSlots.Length > 0)
        {
            var guid = (ulong)session.InWorldGuid;
            var pkt = PlayerSpawn.BuildPlayerValuesUpdate(guid, m =>
            {
                foreach (var slot in mainSlots)
                {
                    var it = BagInventory.MainItemAt(session, slot);
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
            var it = BagInventory.ItemAt(session, bag, slot);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                ContainerObject.BuildSlotUpdate(bagGuid, slot, it is null ? 0UL : ItemObject.ItemGuid(it.ItemGuid)), ct);
        }
    }

    /// <summary>Обновляет ITEM_FIELD_CONTAINED предмета: внутри сумки — guid сумки, иначе — guid игрока. M6.13.</summary>
    internal Task SendContainedAsync(WorldSession session, InventoryItem item, CancellationToken ct)
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

    /// <summary>
    /// SMSG_INVENTORY_CHANGE_FAILURE (3.3.5): отклоняет операцию инвентаря и ВОЗВРАЩАЕТ предмет с курсора.
    /// Без него клиент держит «поднятый» предмет (серый в слоте) до релога — отсюда залипание сумки. M6.13.
    /// Формат: u8 result; если result≠OK — u64 item1, u64 item2, u8 bag_subclass(0). (Level-поле только для
    /// CANT_EQUIP_LEVEL_I=1, нам не нужно.)
    /// </summary>
    internal Task SendEquipErrorAsync(WorldSession session, byte result, ulong item1, ulong item2, CancellationToken ct)
    {
        var w = new ByteWriter(20).UInt8(result);
        if (result != 0)
            w.UInt64(item1).UInt64(item2).UInt8(0);
        return session.SendAsync(WorldOpcode.SmsgInventoryChangeFailure, w.ToArray(), ct);
    }

    /// <summary>Отказ авто-операции: вернуть предмет с курсора (SMSG_INVENTORY_CHANGE_FAILURE) + пере-утвердить. M6.13.</summary>
    internal async Task RejectEquipAsync(WorldSession session, byte err, InventoryItem item,
        CancellationToken ct, params (int Bag, int Slot)[] positions)
    {
        await SendEquipErrorAsync(session, err, ItemObject.ItemGuid(item.ItemGuid), 0, ct);
        await ReassertPosAsync(session, ct, positions);
    }
}
