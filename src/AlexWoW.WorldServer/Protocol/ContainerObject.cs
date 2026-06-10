using AlexWoW.Common.Network;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Сборка SMSG_UPDATE_OBJECT для сумок-контейнеров (TYPEID_CONTAINER, item class=1). Контейнер — это предмет
/// (наследует поля ITEM) + хвост CONTAINER_FIELD_NUM_SLOTS / CONTAINER_FIELD_SLOT_1.. (guid на слот, stride 2).
/// Без этих полей клиент 3.3.5 трактует class=1 как контейнер и читает неинициализированные слоты → краш
/// (M7 #31). HIGHGUID контейнера = как у предмета (0x4700) — guid берём через <see cref="ItemObject.ItemGuid"/>.
/// Сверено с CMaNGOS Bag.cpp / UpdateFields.h: TYPEID_CONTAINER=2, ObjectType=0x07, NUM_SLOTS=0x40, SLOT_1=0x42.
/// </summary>
public static class ContainerObject
{
    /// <summary>Блок CREATE_OBJECT2 для одной сумки: поля предмета + NUM_SLOTS + слоты 0..numSlots-1
    /// (обнулённые, затем заполненные guid'ами содержимого <paramref name="contents"/> — чтобы при релоге
    /// сумка показывала свои предметы). M6.13.</summary>
    public static void WriteCreateBlock(ByteWriter w, ulong guid, uint entry, ulong owner,
        uint stackCount, uint maxDurability, uint numSlots,
        IReadOnlyList<(int Slot, ulong Guid)>? contents = null)
    {
        w.UInt8(UpdateType.CreateObject2);
        PackedGuid.Write(w, guid);
        w.UInt8(TypeId.Container);

        // movement-блок неживого объекта: флаги + high-часть GUID (UPDATEFLAG_HIGHGUID) — как у предмета.
        w.UInt16((ushort)ObjectUpdateFlags.HighGuid);
        w.UInt32((uint)((guid >> 52) & 0xFFF));

        var m = new UpdateMask();
        m.SetUInt64(UpdateField.ObjectGuid, guid);
        m.SetUInt32(UpdateField.ObjectType, TypeMask.ContainerObject);
        m.SetUInt32(UpdateField.ObjectEntry, entry);
        m.SetFloat(UpdateField.ObjectScaleX, 1.0f);
        m.SetUInt64(UpdateField.ItemOwner, owner);
        m.SetUInt64(UpdateField.ItemContained, owner);     // сумка лежит у игрока (рюкзак/bag-слот)
        m.SetUInt32(UpdateField.ItemStackCount, stackCount == 0 ? 1u : stackCount);
        m.SetUInt32(UpdateField.ItemDurability, maxDurability);
        m.SetUInt32(UpdateField.ItemMaxDurability, maxDurability);
        m.SetUInt32(UpdateField.ContainerNumSlots, numSlots);
        for (var i = 0; i < numSlots; i++)
            m.SetUInt64(UpdateField.ContainerSlotGuid(i), 0UL); // слоты инициализируем пустыми
        if (contents is not null)
        {
            foreach (var (slot, itemGuid) in contents)
            {
                if (slot >= 0 && slot < numSlots)
                    m.SetUInt64(UpdateField.ContainerSlotGuid(slot), itemGuid); // заполнить содержимым
            }
        }

        m.WriteTo(w);
    }

    /// <summary>VALUES-апдейт одного слота сумки (CONTAINER_FIELD_SLOT_x = guid предмета или 0). M6.13 (B3/B4).</summary>
    public static byte[] BuildSlotUpdate(ulong bagGuid, int innerSlot, ulong itemGuid)
    {
        var m = new UpdateMask();
        m.SetUInt64(UpdateField.ContainerSlotGuid(innerSlot), itemGuid);
        var w = new ByteWriter(32);
        w.UInt32(1);
        w.UInt8(UpdateType.Values);
        PackedGuid.Write(w, bagGuid);
        m.WriteTo(w);
        return w.ToArray();
    }
}
