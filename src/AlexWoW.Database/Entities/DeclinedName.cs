namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>character_declined_names</c> (ruRU-склонения). Срез 2 рефактора DAL (#23).</summary>
public sealed class DeclinedName
{
    public uint OwnerGuid { get; set; }      // PK (= guid персонажа, НЕ авто-инкремент)
    public string N0 { get; set; } = string.Empty;   // varchar(24) DEFAULT ''
    public string N1 { get; set; } = string.Empty;
    public string N2 { get; set; } = string.Empty;
    public string N3 { get; set; } = string.Empty;
    public string N4 { get; set; } = string.Empty;
}
