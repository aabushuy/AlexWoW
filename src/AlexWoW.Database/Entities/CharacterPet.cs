namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>character_pet</c> (БД alexwow_auth). PET.T5.</summary>
public sealed class CharacterPet
{
    public uint Id { get; set; }
    public uint OwnerGuid { get; set; }
    public uint Entry { get; set; }
    public string Name { get; set; } = "";
    public byte Level { get; set; }
    public uint Experience { get; set; }
    public uint Health { get; set; }
    public uint MaxHealth { get; set; }
    public uint Mana { get; set; }
    public uint MaxMana { get; set; }
    public byte Type { get; set; }
    public byte ReactState { get; set; }
    public byte CommandState { get; set; }
    public uint Happiness { get; set; }
    public DateTime SummonedAt { get; set; }
}
