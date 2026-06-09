namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>character_aura</c> (БД alexwow_auth). Срез 2 рефактора DAL (#23).</summary>
public sealed class CharacterAura
{
    public uint OwnerGuid { get; set; }      // PK часть 1
    public uint Spell { get; set; }          // PK часть 2
    public byte Form { get; set; }           // DEFAULT 0
    /// <summary>Остаток длительности (мс) для временны́х баффов/HoT (M10.5); 0 — перманентный переключатель.</summary>
    public uint RemainingMs { get; set; }    // DEFAULT 0
}
