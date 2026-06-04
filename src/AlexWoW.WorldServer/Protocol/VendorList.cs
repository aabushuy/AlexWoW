using AlexWoW.Common.Network;
using AlexWoW.Database.Models;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Сборка SMSG_LIST_INVENTORY (3.3.5a). Layout сверен с локальной копией wow_messages
/// (struct ListInventoryItem, versions "2.4.3 3" — с extended_cost).
/// </summary>
public static class VendorList
{
    private const int MaxItems = 255; // amount_of_items — u8

    public static byte[] Build(ulong vendorGuid, IReadOnlyList<VendorItem> items)
    {
        var count = Math.Min(items.Count, MaxItems);
        var w = new ByteWriter(8 + 1 + count * 32);
        w.UInt64(vendorGuid);
        w.UInt8((byte)count);
        if (count == 0)
        {
            w.UInt8(0); // «у вендора нет товаров»
            return w.ToArray();
        }

        for (var i = 0; i < count; i++)
        {
            var it = items[i];
            w.UInt32((uint)(i + 1))                                   // muid (1-based)
             .UInt32(it.ItemId)
             .UInt32(it.DisplayId)
             .UInt32(it.MaxCount == 0 ? 0xFFFFFFFFu : it.MaxCount)    // запас (0xFFFFFFFF = бесконечно)
             .UInt32(it.BuyPrice)
             .UInt32(it.MaxDurability)
             .UInt32(it.BuyCount == 0 ? 1u : it.BuyCount)             // размер пачки за покупку
             .UInt32(0);                                             // extended_cost (только золото)
        }
        return w.ToArray();
    }
}
