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

    /// <summary>Один SMSG_UPDATE_OBJECT, создающий все предметы инвентаря (по блоку на предмет).</summary>
    public static byte[] BuildItemsCreate(IReadOnlyList<InventoryItem> items, ulong ownerGuid,
        IReadOnlyDictionary<uint, ItemTemplateData>? templates = null)
    {
        var w = new ByteWriter(64 + items.Count * 96);
        w.UInt32((uint)items.Count); // количество блоков
        foreach (var item in items)
        {
            var maxDur = templates is not null && templates.TryGetValue(item.ItemEntry, out var t)
                ? t.MaxDurability : 0u;
            WriteCreateBlock(w, ItemGuid(item.ItemGuid), item.ItemEntry, ownerGuid, item.StackCount, maxDur);
        }
        return w.ToArray();
    }

    private static void WriteCreateBlock(ByteWriter w, ulong itemGuid, uint entry, ulong owner,
        uint stackCount, uint maxDurability)
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
        m.SetUInt64(UpdateField.ItemContained, owner); // рюкзак/экипировка — контейнер = сам игрок
        m.SetUInt32(UpdateField.ItemStackCount, stackCount == 0 ? 1u : stackCount);
        m.SetUInt32(UpdateField.ItemDurability, maxDurability);
        m.SetUInt32(UpdateField.ItemMaxDurability, maxDurability);
        m.WriteTo(w);
    }
}
