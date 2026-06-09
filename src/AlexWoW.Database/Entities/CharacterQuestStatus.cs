namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>character_queststatus</c> (БД alexwow_auth). Срез 2 рефактора DAL (#23).</summary>
public sealed class CharacterQuestStatus
{
    public uint OwnerGuid { get; set; }      // PK часть 1
    public uint QuestId { get; set; }        // PK часть 2
    public byte Slot { get; set; }           // DEFAULT 0
    public byte Status { get; set; }         // DEFAULT 0
    public ushort Counter0 { get; set; }     // DEFAULT 0
    public ushort Counter1 { get; set; }     // DEFAULT 0
    public ushort Counter2 { get; set; }     // DEFAULT 0
    public ushort Counter3 { get; set; }     // DEFAULT 0
}
