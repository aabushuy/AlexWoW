namespace AlexWoW.Database.Models;

/// <summary>
/// Экземпляр предмета во владении персонажа (строка таблицы character_items).
/// Слоты: экипировка 0..18, сумки 19..22, рюкзак 23..38 (INVENTORY_SLOT_* 3.3.5a).
/// bag = 255 (INVENTORY_SLOT_BAG_0) — основной контейнер (экипировка + рюкзак).
/// </summary>
public sealed class InventoryItem
{
    public uint ItemGuid { get; init; }   // low-counter; полный GUID = HIGHGUID_ITEM | ItemGuid
    public uint OwnerGuid { get; init; }
    public uint ItemEntry { get; init; }
    public byte Bag { get; init; } = 255;
    public byte Slot { get; init; }
    public uint StackCount { get; init; } = 1;
}

/// <summary>Товар вендора (npc_vendor ⨝ item_template) — для SMSG_LIST_INVENTORY и покупки. M6.2.</summary>
public sealed class VendorItem
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

/// <summary>Стартовый предмет (playercreateinfo_item ⨝ item_template) — для раскладки набора.</summary>
public sealed class StartingItem
{
    public uint ItemId { get; init; }
    public byte Amount { get; init; } = 1;
    public byte InventoryType { get; init; }  // item_template.InventoryType → слот экипировки
    public int Stackable { get; init; } = 1;
}
