namespace AlexWoW.Database.Models;

/// <summary>Квест в списке квестгивера (creature_questrelation ⨝ quest_template) — для SMSG_QUESTGIVER_QUEST_LIST. M6.5.</summary>
public sealed record GiverQuest
{
    public uint QuestId { get; init; }
    public int QuestLevel { get; init; }
    public uint QuestFlags { get; init; }
    public string Title { get; init; } = string.Empty;
    public int MinLevel { get; init; }
    public uint RequiredRaces { get; init; }
    public uint RequiredClasses { get; init; }
    public uint PrevQuestId { get; init; }
}
