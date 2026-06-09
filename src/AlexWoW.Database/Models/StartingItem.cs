namespace AlexWoW.Database.Models;

/// <summary>Стартовый предмет (playercreateinfo_item ⨝ item_template) — для раскладки набора.</summary>
public sealed record StartingItem
{
    public uint ItemId { get; init; }
    public byte Amount { get; init; } = 1;
    public byte InventoryType { get; init; }  // item_template.InventoryType → слот экипировки
    public int Stackable { get; init; } = 1;
}
