namespace AlexWoW.Database.Models;

/// <summary>Строка связи существо↔квест (creature_questrelation / creature_involvedrelation). M6.10.</summary>
public sealed record QuestRelation
{
    public uint Id { get; init; }     // entry существа
    public uint Quest { get; init; }  // id квеста
}
