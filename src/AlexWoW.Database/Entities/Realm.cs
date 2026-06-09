namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>realmlist</c> (БД alexwow_auth). Срез 2 рефактора DAL (#23).</summary>
public sealed class Realm
{
    public uint Id { get; set; }
    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;
    public ushort Port { get; set; }       // DEFAULT 8085
    public byte Type { get; set; }          // DEFAULT 0
    public byte Flags { get; set; }         // DEFAULT 0
    public byte Timezone { get; set; }      // DEFAULT 1
    public float Population { get; set; }    // DEFAULT 0
}
