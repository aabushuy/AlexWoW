namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>character_spell</c> (БД alexwow_auth). Срез 2 рефактора DAL (#23).</summary>
public sealed class CharacterSpell
{
    public uint OwnerGuid { get; set; }      // PK часть 1
    public uint Spell { get; set; }          // PK часть 2
}
