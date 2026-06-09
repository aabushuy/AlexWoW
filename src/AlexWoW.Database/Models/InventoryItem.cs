namespace AlexWoW.Database.Models;

/// <summary>
/// Экземпляр предмета во владении персонажа (строка таблицы character_items).
/// Слоты: экипировка 0..18, сумки 19..22, рюкзак 23..38 (INVENTORY_SLOT_* 3.3.5a).
/// bag = 255 (INVENTORY_SLOT_BAG_0) — основной контейнер (экипировка + рюкзак).
/// Остаётся class (а не record): экземпляр мутируется в рантайме (Bag/Slot/StackCount) и
/// идентифицируется по ItemGuid, а не по значению.
/// </summary>
public sealed class InventoryItem
{
    public uint ItemGuid { get; init; }   // low-counter; полный GUID = HIGHGUID_ITEM | ItemGuid
    public uint OwnerGuid { get; init; }
    public uint ItemEntry { get; init; }
    public byte Bag { get; set; } = 255;            // меняется при перемещении (M6.9)
    public byte Slot { get; set; }
    public uint StackCount { get; set; } = 1;
}
