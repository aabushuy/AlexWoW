namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>character_talent</c> (БД alexwow_auth) — изученные таланты. M9.7.
/// Один спек (дуал-спек отложен). rank — 0-индексный (0..4).</summary>
public sealed class CharacterTalent
{
    public uint OwnerGuid { get; set; }   // PK часть 1 (guid персонажа)
    public uint TalentId { get; set; }    // PK часть 2 (Talent.dbc TalentID)
    public byte Rank { get; set; }        // текущий ранг 0..4
}
