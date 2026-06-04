using AlexWoW.Common.Network;
using AlexWoW.Database.Models;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>Сборка тела пакета SMSG_CHAR_ENUM (layout 3.3.5a).</summary>
public static class CharEnum
{
    // EQUIPMENT_SLOT_END(19) + 4 слота сумок = INVENTORY_SLOT_BAG_END.
    private const int EquipmentSlots = 23;

    /// <summary>
    /// CHARACTER_FLAG_DECLINED (3.3.5a, как в CMaNGOS/TrinityCore). Выставлен в per-char flags —
    /// ruRU-клиент знает, что склонения имени заданы, и НЕ показывает диалог склонений на char-select.
    /// </summary>
    private const uint CharacterFlagDeclined = 0x02000000;

    /// <summary>Экипировка слота для paperdoll: displayInfoId + inventoryType (3.3.5a).</summary>
    public readonly record struct SlotDisplay(uint DisplayId, byte InvType);

    public static byte[] BuildBody(IReadOnlyList<Character> characters,
        IReadOnlyDictionary<uint, SlotDisplay[]>? equipment = null,
        IReadOnlySet<uint>? declinedNameGuids = null)
    {
        var w = new ByteWriter(64 + characters.Count * 300);
        w.UInt8((byte)characters.Count);

        foreach (var c in characters)
        {
            w.UInt64(c.Guid)            // player GUID (high part = 0)
             .CString(c.Name)
             .UInt8(c.Race)
             .UInt8(c.Class)
             .UInt8(c.Gender)
             .UInt8(c.Skin)
             .UInt8(c.Face)
             .UInt8(c.HairStyle)
             .UInt8(c.HairColor)
             .UInt8(c.FacialHair)
             .UInt8(c.Level)
             .UInt32(c.Zone)
             .UInt32(c.Map)
             .Single(c.X)
             .Single(c.Y)
             .Single(c.Z)
             .UInt32(0)                 // guild id
             .UInt32(declinedNameGuids?.Contains(c.Guid) == true ? CharacterFlagDeclined : 0u) // character flags
             .UInt32(0)                 // customization flags (recustomize)
             .UInt8(0)                  // first login (intro cinematic)
             .UInt32(0)                 // pet display id
             .UInt32(0)                 // pet level
             .UInt32(0);                // pet family

            // Экипировка: для каждого слота displayId(4) + invType(1) + enchant(4) (paperdoll).
            var slots = equipment is not null && equipment.TryGetValue(c.Guid, out var e) ? e : null;
            for (var slot = 0; slot < EquipmentSlots; slot++)
            {
                var d = slots is not null && slot < slots.Length ? slots[slot] : default;
                w.UInt32(d.DisplayId).UInt8(d.InvType).UInt32(0);
            }
        }

        return w.ToArray();
    }
}
