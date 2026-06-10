using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Диалоговые окна квестов и выдача награды (M6.5/M6.10, DI-сервис M7 S5 — вынос из god-класса
/// QuestHandlers): детали квеста, окно сдачи, проверка пригодности, сдача с наградой (деньги/предметы —
/// через <see cref="InventoryGrantService"/>). Прогресс/персист — <see cref="QuestProgressService"/>.
/// </summary>
internal sealed class QuestDialogService(
    InventoryGrantService inventoryGrant,
    QuestProgressService questProgress,
    QuestGiverStatusService giverStatus,
    IWorldRepository worldDb,
    ICharacterRepository characters)
{
    /// <summary>Может ли игрок взять квест: уровень, раса, класс, предыдущий квест, не в журнале/не сдан. M6.5.
    /// Чистая функция от сессии и квеста — static, зовётся и из <see cref="QuestGiverStatusService"/>.</summary>
    internal static bool CanTakeQuest(WorldSession session, Database.Models.GiverQuest q)
    {
        var c = session.Character;
        if (c is null)
            return false;
        if (q.MinLevel > c.Level)
            return false;
        if (q.RequiredRaces != 0 && (q.RequiredRaces & (1u << (c.Race - 1))) == 0)
            return false;
        if (q.RequiredClasses != 0 && (q.RequiredClasses & (1u << (c.Class - 1))) == 0)
            return false;
        if (q.PrevQuestId != 0 && !session.Quest.CompletedQuests.Contains(q.PrevQuestId))
            return false;
        if (session.Quest.CompletedQuests.Contains(q.QuestId))
            return false;
        foreach (var s in session.Quest.QuestSlots)
            if (s?.QuestId == q.QuestId)
                return false; // уже в журнале
        return true;
    }

    /// <summary>Грузит квест + displayId предметов-наград и шлёт SMSG_QUESTGIVER_QUEST_DETAILS.</summary>
    internal async Task SendQuestDetailsAsync(WorldSession session, ulong npcGuid, uint questId, CancellationToken ct)
    {
        var quest = await worldDb.GetQuestAsync(questId, ct);
        if (quest is null)
            return;

        var rewardEntries = quest.RewItemId.Concat(quest.RewChoiceItemId).Where(id => id != 0).ToArray();
        IReadOnlyDictionary<uint, (uint DisplayId, byte InvType)> displays =
            rewardEntries.Length > 0
                ? await worldDb.GetItemDisplaysAsync(rewardEntries, ct)
                : new Dictionary<uint, (uint, byte)>();

        await session.SendAsync(WorldOpcode.SmsgQuestgiverQuestDetails,
            QuestPackets.BuildQuestDetails(npcGuid, quest, displays), ct);
    }

    /// <summary>Шлёт окно сдачи (SMSG_QUESTGIVER_OFFER_REWARD) с наградами квеста.</summary>
    internal async Task SendOfferRewardAsync(WorldSession session, ulong npcGuid, uint questId, CancellationToken ct)
    {
        var quest = await worldDb.GetQuestAsync(questId, ct);
        if (quest is null)
            return;
        var rewardEntries = quest.RewItemId.Concat(quest.RewChoiceItemId).Where(id => id != 0).ToArray();
        IReadOnlyDictionary<uint, (uint DisplayId, byte InvType)> displays = rewardEntries.Length > 0
            ? await worldDb.GetItemDisplaysAsync(rewardEntries, ct)
            : new Dictionary<uint, (uint, byte)>();
        await session.SendAsync(WorldOpcode.SmsgQuestgiverOfferReward,
            QuestPackets.BuildOfferReward(npcGuid, quest, displays), ct);
    }

    /// <summary>
    /// Сдача квеста с выбором награды (CMSG_QUESTGIVER_CHOOSE_REWARD): списать квест-предметы целей,
    /// выдать деньги/предметы, убрать из журнала и пометить сданным (персист). M6.5/M6.10.
    /// </summary>
    internal async Task ChooseRewardAsync(WorldSession session, uint questId, uint rewardIndex, CancellationToken ct)
    {
        var slot = Array.FindIndex(session.Quest.QuestSlots, s => s?.QuestId == questId);
        if (slot < 0 || session.Quest.QuestSlots[slot] is not { Complete: true })
            return;
        var quest = await worldDb.GetQuestAsync(questId, ct);
        if (quest is null)
            return;

        // M6.10: списать квест-предметы цели (освобождает слоты под предметы-награды).
        for (var i = 0; i < 4; i++)
            if (quest.ReqItemId[i] != 0 && quest.ReqItemCount[i] > 0)
                await inventoryGrant.ConsumeAsync(session, quest.ReqItemId[i], quest.ReqItemCount[i], ct);

        // Деньги-награда.
        if (quest.RewOrReqMoney > 0)
        {
            session.Inv.Money += (uint)quest.RewOrReqMoney;
            await characters.SetMoneyAsync(session.InWorldGuid, session.Inv.Money, ct);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                PlayerSpawn.BuildCoinageUpdate((ulong)session.InWorldGuid, session.Inv.Money), ct);
        }
        // Гарантированные предметы + выбранный из choice.
        for (var i = 0; i < 4; i++)
            if (quest.RewItemId[i] != 0)
                await inventoryGrant.TryGiveAsync(session, quest.RewItemId[i], quest.RewItemCount[i] == 0 ? 1 : quest.RewItemCount[i], ct);
        if (rewardIndex < 6 && quest.RewChoiceItemId[rewardIndex] != 0)
            await inventoryGrant.TryGiveAsync(session, quest.RewChoiceItemId[rewardIndex],
                quest.RewChoiceItemCount[rewardIndex] == 0 ? 1 : quest.RewChoiceItemCount[rewardIndex], ct);

        // Убрать из журнала, пометить сданным.
        session.Quest.QuestSlots[slot] = null;
        session.Quest.CompletedQuests.Add(questId);
        await questProgress.MarkRewardedAsync(session, questId, ct); // M6.10: персист сдачи
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.QuestLogSlotId(slot), 0);
                m.SetUInt32(UpdateField.QuestLogSlotState(slot), 0);
                m.SetUInt32(UpdateField.QuestLogSlotCounters01(slot), 0);
                m.SetUInt32(UpdateField.QuestLogSlotCounters23(slot), 0);
            }), ct);
        await session.SendAsync(WorldOpcode.SmsgQuestgiverQuestComplete, QuestPackets.BuildQuestComplete(quest), ct);
        await giverStatus.PushQuestStatusesAsync(session, ct); // M7 #18: «?» пропадает после сдачи
        session.Logger.LogInformation("QUEST TURN-IN '{User}': quest={Quest} (деньги {Money})", session.Account, questId, quest.RewOrReqMoney);
    }
}
