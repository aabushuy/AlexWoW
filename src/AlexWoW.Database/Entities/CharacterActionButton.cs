namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>character_action</c> (ярлыки панелей). Срез 2 рефактора DAL (#23).</summary>
public sealed class CharacterActionButton
{
    public uint OwnerGuid { get; set; }      // PK часть 1
    public byte Button { get; set; }         // PK часть 2
    public uint PackedData { get; set; }
}
