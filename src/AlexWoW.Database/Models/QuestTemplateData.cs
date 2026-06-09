namespace AlexWoW.Database.Models;

/// <summary>Шаблон квеста (quest_template) — нужные поля для деталей/награды/целей. M6.5.</summary>
public sealed record QuestTemplateData
{
    public uint Entry { get; init; }
    public int QuestLevel { get; init; }
    public int MinLevel { get; init; }
    public int ZoneOrSort { get; init; }
    public uint Type { get; init; }
    public uint Method { get; init; }
    public uint SrcItemId { get; init; }
    public uint NextQuestId { get; init; }
    public uint QuestFlags { get; init; }
    public uint SuggestedPlayers { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string Objectives { get; init; } = string.Empty;
    public string OfferRewardText { get; init; } = string.Empty;
    public string RequestItemsText { get; init; } = string.Empty;
    public string EndText { get; init; } = string.Empty;
    /// <summary>Деньги: &gt;0 — награда, &lt;0 — требуется (для нашего инкремента берём как награду, если &gt;0).</summary>
    public int RewOrReqMoney { get; init; }
    /// <summary>Индекс XP-награды (quest_template.RewXPId). В 3.3.x — отдельное поле в SMSG_QUEST_QUERY_RESPONSE
    /// между next_quest_in_chain и money_reward; без него съезжает массив наград. M6.5.</summary>
    public uint RewXpId { get; init; }
    public uint RewSpell { get; init; }
    public uint RewSpellCast { get; init; }
    public uint[] RewItemId { get; init; } = new uint[4];
    public uint[] RewItemCount { get; init; } = new uint[4];
    public uint[] RewChoiceItemId { get; init; } = new uint[6];
    public uint[] RewChoiceItemCount { get; init; } = new uint[6];
    /// <summary>Цели-убийства/гео (отрицательное = GO): entry + count, до 4. M6.5 инкр.3.</summary>
    public int[] ReqCreatureOrGoId { get; init; } = new int[4];
    public uint[] ReqCreatureOrGoCount { get; init; } = new uint[4];
    public uint[] ReqItemId { get; init; } = new uint[6];
    public uint[] ReqItemCount { get; init; } = new uint[6];
    public string[] ObjectiveText { get; init; } = new string[4];
}
