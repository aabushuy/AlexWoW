namespace AlexWoW.Database.Models;

/// <summary>Товар вендора (npc_vendor ⨝ item_template) — для SMSG_LIST_INVENTORY и покупки. M6.2.</summary>
public sealed record VendorItem
{
    public uint Slot { get; init; }        // npc_vendor.slot (порядок в окне)
    public uint ItemId { get; init; }
    public uint MaxCount { get; init; }    // 0 = бесконечный запас
    public uint BuyPrice { get; init; }    // цена покупки (медь) из item_template
    public uint DisplayId { get; init; }
    public uint MaxDurability { get; init; }
    public uint BuyCount { get; init; }     // размер пачки за одну покупку
    public string Name { get; init; } = string.Empty;
    public int Stackable { get; init; } = 1;
}
