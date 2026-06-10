namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Слоты инвентаря персонажа (3.3.5a) и маппинг InventoryType → слот экипировки.
/// Слот — индекс в основном контейнере (bag = INVENTORY_SLOT_BAG_0 = 255):
/// экипировка 0..18, сумки 19..22, рюкзак 23..38.
/// </summary>
public static class InventorySlots
{
    public const byte MainBag = 255; // INVENTORY_SLOT_BAG_0

    public const int EquipmentStart = 0;
    public const int EquipmentEnd = 19;    // EQUIPMENT_SLOT_END (слоты 0..18)
    public const byte MainHandSlot = 15;   // EQUIPMENT_SLOT_MAINHAND (M9.2 — урон оружия)
    public const int BagSlotStart = 19;    // INVENTORY_SLOT_BAG_START (надеваемые сумки)
    public const int BagSlotEnd = 23;      // INVENTORY_SLOT_BAG_END (слоты 19..22)
    public const int BackpackStart = 23;   // INVENTORY_SLOT_ITEM_START
    public const int BackpackEnd = 39;     // INVENTORY_SLOT_ITEM_END (слоты 23..38)

    /// <summary>InventoryType сумки (INVTYPE_BAG). M6.13.</summary>
    public const uint InvTypeBag = 18;

    /// <summary>Слот экипировки (0..18)?</summary>
    public static bool IsEquipmentSlot(int slot) => slot >= EquipmentStart && slot < EquipmentEnd;

    /// <summary>Слот надеваемой сумки (19..22)? M6.13.</summary>
    public static bool IsBagSlot(int slot) => slot >= BagSlotStart && slot < BagSlotEnd;

    /// <summary>Слот основного рюкзака (23..38)?</summary>
    public static bool IsBackpackSlot(int slot) => slot >= BackpackStart && slot < BackpackEnd;

    /// <summary>Можно ли надеть предмет данного InventoryType в конкретный слот экипировки.</summary>
    public static bool CanEquipInSlot(uint inventoryType, int slot)
    {
        if (EquipSlotFor(inventoryType) == slot)
            return true;
        return inventoryType switch
        {
            11 => slot is 10 or 11,          // FINGER → любое кольцо
            12 => slot is 12 or 13,          // TRINKET → любой тринкет
            13 or 21 => slot is 15 or 16,    // 1H/MAINHAND → осн./доп. рука
            _ => false,
        };
    }

    /// <summary>InventoryType из item_template → слот экипировки (0..18), либо -1 (не экипируется).</summary>
    public static int EquipSlotFor(uint inventoryType) => inventoryType switch
    {
        1 => 0,    // HEAD
        2 => 1,    // NECK
        3 => 2,    // SHOULDERS
        4 => 3,    // BODY (рубаха)
        5 or 20 => 4,   // CHEST / ROBE
        6 => 5,    // WAIST
        7 => 6,    // LEGS
        8 => 7,    // FEET
        9 => 8,    // WRISTS
        10 => 9,   // HANDS
        11 => 10,  // FINGER (первое кольцо)
        12 => 12,  // TRINKET (первый тринкет)
        16 => 14,  // CLOAK
        13 or 21 or 17 => 15,  // WEAPON / MAINHAND / 2HWEAPON
        14 or 22 or 23 => 16,  // SHIELD / OFFHAND / HOLDABLE
        15 or 25 or 26 or 28 => 17,  // RANGED / THROWN / RANGEDRIGHT / RELIC
        19 => 18,  // TABARD
        _ => -1,   // не экипируется → рюкзак
    };
}
