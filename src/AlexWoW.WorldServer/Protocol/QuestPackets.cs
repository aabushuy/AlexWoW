using AlexWoW.Common.Network;
using AlexWoW.Database.Models;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>Сборка квест-пакетов (M6.5): список квестов NPC и окно деталей квеста.</summary>
public static class QuestPackets
{
    /// <summary>Иконка квеста в списке (доступен). Подбирается под клиент 3.3.5.</summary>
    private const uint QuestIconAvailable = 2;

    /// <summary>QUEST_FLAGS_AUTO_ACCEPT — с ним клиент считает квест авто-принимаемым, и ручная кнопка
    /// «Принять» становится no-op (окно закрывается, accept не шлётся). Снимаем при отправке клиенту,
    /// т.к. серверного авто-взятия пока нет. M6.5.</summary>
    private const uint QuestFlagAutoAccept = 0x80000;
    private static uint ClientFlags(uint questFlags) => questFlags & ~QuestFlagAutoAccept;

    /// <summary>
    /// SMSG_QUESTGIVER_QUEST_LIST (3.3.5): Guid npc + CString greeting + u32 emote_delay + u32 emote +
    /// u8 count + [u32 quest_id + u32 icon + i32 level + u32 flags + u8 repeatable + CString title].
    /// </summary>
    public static byte[] BuildQuestList(ulong npcGuid, string greeting, IReadOnlyList<GiverQuest> quests)
    {
        var w = new ByteWriter(64);
        w.UInt64(npcGuid);
        w.CString(greeting);
        w.UInt32(0);   // emote_delay
        w.UInt32(1);   // emote (1 = talk)
        w.UInt8((byte)Math.Min(quests.Count, 32));
        foreach (var q in quests.Take(32))
            w.UInt32(q.QuestId)
             .UInt32(QuestIconAvailable)
             .Int32(q.QuestLevel)
             .UInt32(ClientFlags(q.QuestFlags))
             .UInt8(0)            // repeatable
             .CString(q.Title);
        return w.ToArray();
    }

    /// <summary>
    /// SMSG_QUESTGIVER_QUEST_DETAILS (3.3.5). Окно деталей квеста с кнопкой «Принять».
    /// <paramref name="displays"/> — displayId по entry предметов-наград (для иконок).
    /// </summary>
    public static byte[] BuildQuestDetails(ulong npcGuid, QuestTemplateData q,
        IReadOnlyDictionary<uint, (uint DisplayId, byte InvType)> displays)
    {
        var w = new ByteWriter(256);
        w.UInt64(npcGuid);
        w.UInt64(0);                 // guid2 (0 — не игрок)
        w.UInt32(q.Entry);
        w.CString(q.Title);
        w.CString(q.Details);
        w.CString(q.Objectives);
        w.UInt8(0);                  // auto_finish
        w.UInt32(ClientFlags(q.QuestFlags));
        w.UInt32(q.SuggestedPlayers);
        w.UInt8(0);                  // is_finished

        // Награды на выбор + гарантированные (QuestGiverReward: item, count, display).
        WriteRewardItems(w, q.RewChoiceItemId, q.RewChoiceItemCount, displays);
        WriteRewardItems(w, q.RewItemId, q.RewItemCount, displays);

        w.UInt32(q.RewOrReqMoney > 0 ? (uint)q.RewOrReqMoney : 0u); // money_reward
        w.UInt32(0);                 // experience_reward (опыт/уровни — позже)
        w.UInt32(0);                 // honor_reward
        w.Single(0f);                // honor_reward_multiplier
        w.UInt32(q.RewSpell);        // reward_spell
        w.UInt32(q.RewSpellCast);    // casted_spell
        w.UInt32(0);                 // title_reward
        w.UInt32(0);                 // talent_reward
        w.UInt32(0);                 // arena_point_reward
        w.UInt32(0);                 // unknown2
        for (var i = 0; i < 5; i++) w.UInt32(0); // reward_factions
        for (var i = 0; i < 5; i++) w.UInt32(0); // reward_reputations
        for (var i = 0; i < 5; i++) w.UInt32(0); // reward_reputations_override
        w.UInt32(0);                 // amount_of_emotes
        return w.ToArray();
    }

    /// <summary>
    /// SMSG_QUEST_QUERY_RESPONSE (3.3.5) — полные данные квеста для журнала (текст/цели/награды).
    /// Layout по reference/wow_messages (версия 3.3.5). Большой пакет; порядок полей критичен.
    /// </summary>
    public static byte[] BuildQuestQueryResponse(QuestTemplateData q)
    {
        var w = new ByteWriter(512);
        w.UInt32(q.Entry);
        w.UInt32(q.Method == 0 ? 2u : q.Method); // quest_method (2 = обычный)
        w.Int32(q.QuestLevel);
        w.Int32(q.MinLevel);
        w.Int32(q.ZoneOrSort);
        w.UInt32(q.Type);
        w.UInt32(q.SuggestedPlayers);
        w.UInt32(0);                 // reputation_objective_faction
        w.UInt32(0);                 // reputation_objective_value
        w.UInt32(0);                 // required_opposite_faction
        w.UInt32(0);                 // required_opposite_reputation_value
        w.UInt32(q.NextQuestId);     // next_quest_in_chain
        w.UInt32(q.RewXpId);         // reward_xp_id (3.3.x) — БЕЗ него съезжает массив наград (M7 #19)
        w.UInt32(q.RewOrReqMoney > 0 ? (uint)q.RewOrReqMoney : 0u); // money_reward
        w.UInt32(0);                 // max_level_money_reward
        w.UInt32(q.RewSpell);        // reward_spell
        w.UInt32(q.RewSpellCast);    // casted_reward_spell
        w.UInt32(0);                 // honor_reward
        w.Single(0f);                // honor_reward_multiplier
        w.UInt32(q.SrcItemId);       // source_item_id
        w.UInt32(ClientFlags(q.QuestFlags));
        w.UInt32(0);                 // title_reward
        w.UInt32(0);                 // players_slain
        w.UInt32(0);                 // bonus_talents
        w.UInt32(0);                 // bonus_arena_points
        w.UInt32(0);                 // unknown1

        for (var i = 0; i < 4; i++) w.UInt32(q.RewItemId[i]).UInt32(q.RewItemCount[i]);             // rewards[4]
        for (var i = 0; i < 6; i++) w.UInt32(q.RewChoiceItemId[i]).UInt32(q.RewChoiceItemCount[i]); // choice_rewards[6]
        for (var i = 0; i < 5; i++) w.UInt32(0); // reputation_rewards
        for (var i = 0; i < 5; i++) w.UInt32(0); // reputation_reward_amounts
        for (var i = 0; i < 5; i++) w.UInt32(0); // reputation_reward_overrides

        w.UInt32(0);                 // point_map_id
        w.Single(0f).Single(0f);     // position
        w.UInt32(0);                 // point_opt
        // (M7 #19) костыльный лишний u32 убран — недостающие 4 байта были полем reward_xp_id выше.

        w.CString(q.Title);
        w.CString(q.Objectives);     // objective_text
        w.CString(q.Details);
        w.CString(q.EndText);
        w.CString(string.Empty);     // completed_text

        // objectives[4]: creature/GO (GO как id|0x80000000) + kill_count + required_item + count
        for (var i = 0; i < 4; i++)
        {
            var cog = q.ReqCreatureOrGoId[i];
            var creature = cog < 0 ? ((uint)(-cog) | 0x80000000u) : (uint)cog;
            w.UInt32(creature).UInt32(q.ReqCreatureOrGoCount[i]).UInt32(q.ReqItemId[i]).UInt32(q.ReqItemCount[i]);
        }
        // item_requirements[6]: item + count + display (0)
        for (var i = 0; i < 6; i++) w.UInt32(q.ReqItemId[i]).UInt32(q.ReqItemCount[i]).UInt32(0);
        // objective_texts[4]
        for (var i = 0; i < 4; i++) w.CString(q.ObjectiveText[i] ?? string.Empty);
        return w.ToArray();
    }

    /// <summary>SMSG_QUESTGIVER_OFFER_REWARD (3.3.5) — окно сдачи квеста с наградами.</summary>
    public static byte[] BuildOfferReward(ulong npcGuid, QuestTemplateData q,
        IReadOnlyDictionary<uint, (uint DisplayId, byte InvType)> displays)
    {
        var w = new ByteWriter(256);
        w.UInt64(npcGuid);
        w.UInt32(q.Entry);
        w.CString(q.Title);
        w.CString(string.IsNullOrEmpty(q.OfferRewardText) ? q.Details : q.OfferRewardText);
        w.UInt8(1);                  // auto_finish — u8 (НЕ u32! сверено с TrinityCore: uint8(AutoLaunched));
                                     // лишние 3 байта сдвигали весь хвост → money врал (напр. «41943 золота»)
        w.UInt32(ClientFlags(q.QuestFlags));
        w.UInt32(q.SuggestedPlayers);
        w.UInt32(0);                 // amount_of_emotes
        WriteRewardItems(w, q.RewChoiceItemId, q.RewChoiceItemCount, displays);
        WriteRewardItems(w, q.RewItemId, q.RewItemCount, displays);
        w.UInt32(q.RewOrReqMoney > 0 ? (uint)q.RewOrReqMoney : 0u); // money_reward
        w.UInt32(0);                 // experience_reward
        w.UInt32(0);                 // honor_reward
        w.Single(0f);                // honor_reward_multiplier
        w.UInt32(0);                 // unknown1
        w.UInt32(q.RewSpell);        // reward_spell
        w.UInt32(q.RewSpellCast);    // reward_spell_cast
        w.UInt32(0);                 // title_reward
        w.UInt32(0);                 // reward_talents
        w.UInt32(0);                 // reward_arena_points
        w.UInt32(0);                 // reward_reputation_mask
        for (var i = 0; i < 5; i++) w.UInt32(0); // reward_factions
        for (var i = 0; i < 5; i++) w.UInt32(0); // reward_reputations
        for (var i = 0; i < 5; i++) w.UInt32(0); // reward_reputations_override
        return w.ToArray();
    }

    /// <summary>SMSG_QUESTGIVER_QUEST_COMPLETE (3.3.5) — квест завершён (награда выдана).</summary>
    public static byte[] BuildQuestComplete(QuestTemplateData q)
    {
        var w = new ByteWriter(48);
        w.UInt32(q.Entry);
        w.UInt32(0);                 // unknown
        w.UInt32(0);                 // experience_reward
        w.UInt32(q.RewOrReqMoney > 0 ? (uint)q.RewOrReqMoney : 0u); // money_reward
        w.UInt32(0);                 // honor_reward
        w.UInt32(0);                 // talent_reward
        w.UInt32(0);                 // arena_point_reward
        var n = 0;
        for (var i = 0; i < 4; i++) if (q.RewItemId[i] != 0) n++;
        w.UInt32((uint)n);
        for (var i = 0; i < 4; i++)
            if (q.RewItemId[i] != 0)
                w.UInt32(q.RewItemId[i]).UInt32(q.RewItemCount[i] == 0 ? 1u : q.RewItemCount[i]);
        return w.ToArray();
    }

    /// <summary>SMSG_QUESTUPDATE_ADD_KILL (3.3.5): прогресс цели-убийства/разговора.</summary>
    public static byte[] BuildAddKill(uint questId, uint creatureId, uint count, uint required, ulong creatureGuid)
        => new ByteWriter(28).UInt32(questId).UInt32(creatureId).UInt32(count).UInt32(required).UInt64(creatureGuid).ToArray();

    /// <summary>SMSG_QUESTUPDATE_COMPLETE (3.3.5): цель квеста выполнена.</summary>
    public static byte[] BuildUpdateComplete(uint questId)
        => new ByteWriter(4).UInt32(questId).ToArray();

    private static void WriteRewardItems(ByteWriter w, uint[] ids, uint[] counts,
        IReadOnlyDictionary<uint, (uint DisplayId, byte InvType)> displays)
    {
        var n = 0;
        for (var i = 0; i < ids.Length; i++)
            if (ids[i] != 0) n++;
        w.UInt32((uint)n);
        for (var i = 0; i < ids.Length; i++)
        {
            if (ids[i] == 0) continue;
            var display = displays.TryGetValue(ids[i], out var d) ? d.DisplayId : 0u;
            w.UInt32(ids[i]).UInt32(counts[i] == 0 ? 1u : counts[i]).UInt32(display);
        }
    }
}
