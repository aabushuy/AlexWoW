namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>character_items</c> (БД alexwow_auth). Срез 2 рефактора DAL (#23).</summary>
public sealed class CharacterItem
{
    public uint ItemGuid { get; set; }
    public uint OwnerGuid { get; set; }
    public uint ItemEntry { get; set; }
    public byte Bag { get; set; }            // DEFAULT 255
    public byte Slot { get; set; }
    public uint StackCount { get; set; }     // DEFAULT 1
}
