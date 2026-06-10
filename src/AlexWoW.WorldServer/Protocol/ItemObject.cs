using AlexWoW.Common.Network;
using AlexWoW.Database.Models;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Сборка SMSG_UPDATE_OBJECT для предметов (UPDATETYPE_CREATE_OBJECT2, TYPEID_ITEM).
/// Предмет — не Living: movement-блок = флаги UPDATEFLAG_HIGHGUID (0x10) + high-часть GUID
/// (как в CMaNGOS Item::Item / Object::BuildMovementUpdate, 3.3.5a).
/// </summary>
public static class ItemObject
{
    /// <summary>HIGHGUID_ITEM (12-бит 0x470; в 16-битной форме 0x4700, как 0xF130 у юнитов).</summary>
    public const ulong HighGuidItem = 0x4700;

    /// <summary>Полный GUID предмета по low-counter из БД (character_items.item_guid).</summary>
    public static ulong ItemGuid(uint counter) => (HighGuidItem << 48) | counter;

    /// <summary>
    /// Один SMSG_UPDATE_OBJECT, создающий все предметы инвентаря (по блоку на предмет). Предметы-сумки
    /// (class=1, по <paramref name="bagInfo"/>) создаются как TYPEID_CONTAINER (<see cref="ContainerObject"/>) —
    /// иначе клиент крашится, читая поля контейнера у обычного item-объекта (M6.13 / баг #31).
    /// </summary>
    public static byte[] BuildItemsCreate(IReadOnlyList<InventoryItem> items, ulong ownerGuid,
        IReadOnlyDictionary<uint, ItemBagInfo>? bagInfo = null)
    {
        var w = new ByteWriter(64 + items.Count * 96);
        w.UInt32((uint)items.Count); // количество блоков
        foreach (var item in items)
        {
            var info = bagInfo is not null && bagInfo.TryGetValue(item.ItemEntry, out var bi) ? bi : default;
            if (info.IsContainer)
            {
                // Содержимое НАДЕТОЙ сумки (предметы с Bag == слот этой сумки 19..22) — чтобы при релоге
                // её слоты сразу показывали предметы. Сумка в рюкзаке (Slot 23..38) содержимого не имеет.
                List<(int Slot, ulong Guid)>? contents = null;
                if (InventorySlots.IsBagSlot(item.Slot))
                {
                    contents = [.. items.Where(i => i.Bag == item.Slot).Select(i => ((int)i.Slot, ItemGuid(i.ItemGuid)))];
                }

                ContainerObject.WriteCreateBlock(w, ItemGuid(item.ItemGuid), item.ItemEntry, ownerGuid,
                    item.StackCount, info.MaxDurability, info.ContainerSlots, contents);
            }
            else
            {
                // Предмет внутри надетой сумки → ITEM_FIELD_CONTAINED = guid сумки (иначе guid игрока).
                var contained = ownerGuid;
                if (InventorySlots.IsBagSlot(item.Bag))
                {
                    var bagItem = items.FirstOrDefault(i => i.Bag == InventorySlots.MainBag && i.Slot == item.Bag);
                    if (bagItem is not null) contained = ItemGuid(bagItem.ItemGuid);
                }
                WriteCreateBlock(w, ItemGuid(item.ItemGuid), item.ItemEntry, ownerGuid, item.StackCount, info.MaxDurability, contained);
            }
        }
        return w.ToArray();
    }

    /// <summary>VALUES-апдейт размера стопки на уже созданном предмете (ITEM_FIELD_STACK_COUNT). M6.9.</summary>
    public static byte[] BuildStackUpdate(ulong itemGuid, uint stackCount)
    {
        var m = new UpdateMask();
        m.SetUInt32(UpdateField.ItemStackCount, stackCount);
        var w = new ByteWriter(32);
        w.UInt32(1);
        w.UInt8(UpdateType.Values);
        PackedGuid.Write(w, itemGuid);
        m.WriteTo(w);
        return w.ToArray();
    }

    /// <summary>VALUES-апдейт контейнера предмета (ITEM_FIELD_CONTAINED = guid сумки или игрока). M6.13.
    /// Держим синхронно с CONTAINER_FIELD_SLOT_x сумки при перемещении предмета в/из сумки.</summary>
    public static byte[] BuildContainedUpdate(ulong itemGuid, ulong containerGuid)
    {
        var m = new UpdateMask();
        m.SetUInt64(UpdateField.ItemContained, containerGuid);
        var w = new ByteWriter(32);
        w.UInt32(1);
        w.UInt8(UpdateType.Values);
        PackedGuid.Write(w, itemGuid);
        m.WriteTo(w);
        return w.ToArray();
    }

    private static void WriteCreateBlock(ByteWriter w, ulong itemGuid, uint entry, ulong owner,
        uint stackCount, uint maxDurability, ulong contained)
    {
        w.UInt8(UpdateType.CreateObject2);
        PackedGuid.Write(w, itemGuid);
        w.UInt8(TypeId.Item);

        // movement-блок неживого объекта: флаги + high-часть GUID (UPDATEFLAG_HIGHGUID).
        w.UInt16((ushort)ObjectUpdateFlags.HighGuid);
        w.UInt32((uint)((itemGuid >> 52) & 0xFFF));

        var m = new UpdateMask();
        m.SetUInt64(UpdateField.ObjectGuid, itemGuid);
        m.SetUInt32(UpdateField.ObjectType, TypeMask.ItemObject);
        m.SetUInt32(UpdateField.ObjectEntry, entry);
        m.SetFloat(UpdateField.ObjectScaleX, 1.0f);
        m.SetUInt64(UpdateField.ItemOwner, owner);
        m.SetUInt64(UpdateField.ItemContained, contained); // рюкзак/экипировка = игрок; внутри сумки = guid сумки
        m.SetUInt32(UpdateField.ItemStackCount, stackCount == 0 ? 1u : stackCount);
        m.SetUInt32(UpdateField.ItemDurability, maxDurability);
        m.SetUInt32(UpdateField.ItemMaxDurability, maxDurability);
        m.WriteTo(w);
    }
}
