namespace AlexWoW.Database.Entities;

/// <summary>
/// EF-сущность таблицы <c>character_skill</c> (БД alexwow_auth) — навыки персонажа: профессии и
/// прочие скиллы (языки выдаются по расе и не персистятся). M11.1.
/// </summary>
public sealed class CharacterSkill
{
    public uint OwnerGuid { get; set; }   // PK часть 1 (guid персонажа)
    public ushort SkillId { get; set; }   // PK часть 2 (SkillLine.dbc id, напр. 164 = кузнечное)
    public ushort Value { get; set; }     // текущее значение навыка (1..max)
    public ushort Max { get; set; }        // потолок навыка для тира (75/150/225/300/375/450)
    public byte Step { get; set; }         // тир (0..5); для UI шага кнопки «обучить»
}
