using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Квесты (M6.5). Инкремент 1 — иконки квестгиверов («!»/«?»): отвечаем на
/// CMSG_QUESTGIVER_STATUS_QUERY (один NPC) и CMSG_QUESTGIVER_STATUS_MULTIPLE_QUERY (все видимые).
/// Диалог/взятие/сдача/цели — следующие инкременты.
/// </summary>
public static class QuestHandlers
{
    // QuestGiverStatus (3.3.5): иконки. AVAILABLE=8 («!» жёлтый), REWARD=10 («?» жёлтый), NONE=0.
    private const byte StatusNone = 0;
    private const byte StatusAvailable = 8;

    /// <summary>entry шаблона существа из его GUID (0xF130 | entry&lt;&lt;24 | counter).</summary>
    private static uint CreatureEntry(ulong guid) => (uint)((guid >> 24) & 0xFFFFFF);

    /// <summary>
    /// Статус квестгивера для существа (инкремент 1 — без журнала квестов): даёт квест → «!»
    /// (AVAILABLE); приём/в процессе появятся с журналом квестов (инкр.2+).
    /// </summary>
    private static byte StatusFor(WorldSession session, uint entry)
        => session.World.Quests.IsGiver(entry) ? StatusAvailable : StatusNone;

    [WorldOpcodeHandler(WorldOpcode.CmsgQuestgiverStatusQuery)]
    public static async Task OnStatusQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        await session.World.Quests.EnsureLoadedAsync(ct);
        var guid = packet.Reader().UInt64();
        var status = StatusFor(session, CreatureEntry(guid));
        await session.SendAsync(WorldOpcode.SmsgQuestgiverStatus,
            new ByteWriter(12).UInt64(guid).UInt32(status).ToArray(), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgQuestgiverStatusMultipleQuery)]
    public static async Task OnStatusMultipleQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        await session.World.Quests.EnsureLoadedAsync(ct);

        var reports = new List<(ulong Guid, byte Status)>();
        foreach (var (guid, creature) in session.VisibleNpcs)
        {
            var status = StatusFor(session, creature.Template.Entry);
            if (status != StatusNone)
                reports.Add((guid, status));
        }

        var w = new ByteWriter(4 + reports.Count * 9).UInt32((uint)reports.Count);
        foreach (var (guid, status) in reports)
            w.UInt64(guid).UInt8(status);
        await session.SendAsync(WorldOpcode.SmsgQuestgiverStatusMultiple, w.ToArray(), ct);
    }

    // ===================== Диалог квеста: открыть / детали / принять (M6.5 инкр.2) =====================

    /// <summary>
    /// CMSG_GOSSIP_HELLO / CMSG_QUESTGIVER_HELLO (u64 npc): квестгивер → список/детали квестов;
    /// иначе — окно вендора (если NPC торгует). Объединено, т.к. на правый клик клиент шлёт GOSSIP_HELLO.
    /// </summary>
    [WorldOpcodeHandler(WorldOpcode.CmsgGossipHello, WorldOpcode.CmsgQuestgiverHello)]
    public static async Task OnHello(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        await session.World.Quests.EnsureLoadedAsync(ct);
        var npcGuid = packet.Reader().UInt64();
        var entry = CreatureEntry(npcGuid);

        // Разговор засчитывает цели «поговорить с этим NPC».
        await CreditCreatureAsync(session, entry, npcGuid, ct);

        // Сдача: NPC принимает завершённый квест из журнала → окно награды.
        if (session.World.Quests.IsEnder(entry))
        {
            var enderIds = await session.WorldDb.GetEnderQuestIdsAsync(entry, ct);
            var done = Array.Find(session.QuestSlots, s => s is { Complete: true } p && enderIds.Contains(p.QuestId));
            if (done is not null)
            {
                await SendOfferRewardAsync(session, npcGuid, done.QuestId, ct);
                return;
            }
        }

        if (session.World.Quests.IsGiver(entry))
        {
            var all = await session.WorldDb.GetGiverQuestsAsync(entry, ct);
            var quests = all.Where(q => CanTakeQuest(session, q)).ToList(); // фильтр пригодности
            session.Logger.LogDebug("QUEST hello '{User}': npc={Entry}, {Take}/{All} берущихся",
                session.Account, entry, quests.Count, all.Count);
            if (quests.Count == 1)
            {
                await SendQuestDetailsAsync(session, npcGuid, quests[0].QuestId, ct);
                return;
            }
            if (quests.Count > 1)
            {
                await session.SendAsync(WorldOpcode.SmsgQuestgiverQuestList,
                    QuestPackets.BuildQuestList(npcGuid, string.Empty, quests), ct);
                return;
            }
        }

        // Не квестгивер (или без доступных квестов) — попробуем как вендора.
        await VendorHandlers.SendVendorListAsync(session, npcGuid, ct);
    }

    /// <summary>Может ли игрок взять квест: уровень, раса, класс, предыдущий квест, не в журнале/не сдан. M6.5.</summary>
    private static bool CanTakeQuest(WorldSession session, Database.Models.GiverQuest q)
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
        if (q.PrevQuestId != 0 && !session.CompletedQuests.Contains(q.PrevQuestId))
            return false;
        if (session.CompletedQuests.Contains(q.QuestId))
            return false;
        foreach (var s in session.QuestSlots)
            if (s?.QuestId == q.QuestId)
                return false; // уже в журнале
        return true;
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgQuestgiverQueryQuest)]
    public static async Task OnQueryQuest(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var npcGuid = r.UInt64();
        var questId = r.UInt32();
        await SendQuestDetailsAsync(session, npcGuid, questId, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgQuestgiverAcceptQuest)]
    public static async Task OnAcceptQuest(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        r.UInt64();                       // npc guid
        var questId = r.UInt32();
        if (session.InWorldGuid == 0 || questId == 0)
            return;
        if (Array.FindIndex(session.QuestSlots, s => s?.QuestId == questId) >= 0)
            return; // уже в журнале
        var slot = Array.IndexOf(session.QuestSlots, null);
        if (slot < 0)
            return; // журнал полон (SMSG_QUESTLOG_FULL — позже)

        var quest = await session.WorldDb.GetQuestAsync(questId, ct);
        if (quest is null)
            return;

        var prog = new World.QuestProgress
        {
            QuestId = questId,
            HasItemObjectives = quest.ReqItemId.Any(id => id != 0),
        };
        for (var i = 0; i < 4; i++)
        {
            prog.Creature[i] = quest.ReqCreatureOrGoId[i];
            prog.Required[i] = quest.ReqCreatureOrGoCount[i];
        }
        prog.Complete = !prog.HasItemObjectives && prog.CreatureObjectivesMet();
        session.QuestSlots[slot] = prog;

        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid,
                m => m.SetUInt32(UpdateField.QuestLogSlotId(slot), questId)), ct);
        if (prog.Complete) // квест без целей (напр. «поговорить с X», где X — приёмщик) сразу выполнен
            await session.SendAsync(WorldOpcode.SmsgQuestupdateComplete, QuestPackets.BuildUpdateComplete(questId), ct);
        session.Logger.LogInformation("QUEST ACCEPT '{User}': quest={Quest} → слот {Slot} (complete={C})",
            session.Account, questId, slot, prog.Complete);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgQuestQuery)]
    public static async Task OnQuestQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var questId = packet.Reader().UInt32();
        var quest = await session.WorldDb.GetQuestAsync(questId, ct);
        if (quest is null)
            return;
        await session.SendAsync(WorldOpcode.SmsgQuestQueryResponse, QuestPackets.BuildQuestQueryResponse(quest), ct);
    }

    // CMSG_QUESTGIVER_COMPLETE_QUEST / REQUEST_REWARD (Guid + quest) → окно награды.
    [WorldOpcodeHandler(WorldOpcode.CmsgQuestgiverCompleteQuest, WorldOpcode.CmsgQuestgiverRequestReward)]
    public static async Task OnRequestReward(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var npcGuid = r.UInt64();
        var questId = r.UInt32();
        await SendOfferRewardAsync(session, npcGuid, questId, ct);
    }

    // CMSG_QUESTGIVER_CHOOSE_REWARD (Guid + quest + reward index) → выдача награды + завершение.
    [WorldOpcodeHandler(WorldOpcode.CmsgQuestgiverChooseReward)]
    public static async Task OnChooseReward(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        r.UInt64();                       // npc guid
        var questId = r.UInt32();
        var rewardIndex = r.UInt32();

        var slot = Array.FindIndex(session.QuestSlots, s => s?.QuestId == questId);
        if (slot < 0 || session.QuestSlots[slot] is not { Complete: true })
            return;
        var quest = await session.WorldDb.GetQuestAsync(questId, ct);
        if (quest is null)
            return;

        // Деньги-награда.
        if (quest.RewOrReqMoney > 0)
        {
            session.Money += (uint)quest.RewOrReqMoney;
            await session.Characters.SetMoneyAsync(session.InWorldGuid, session.Money, ct);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                PlayerSpawn.BuildCoinageUpdate((ulong)session.InWorldGuid, session.Money), ct);
        }
        // Гарантированные предметы + выбранный из choice.
        for (var i = 0; i < 4; i++)
            if (quest.RewItemId[i] != 0)
                await InventoryGrant.TryGiveAsync(session, quest.RewItemId[i], quest.RewItemCount[i] == 0 ? 1 : quest.RewItemCount[i], ct);
        if (rewardIndex < 6 && quest.RewChoiceItemId[rewardIndex] != 0)
            await InventoryGrant.TryGiveAsync(session, quest.RewChoiceItemId[rewardIndex],
                quest.RewChoiceItemCount[rewardIndex] == 0 ? 1 : quest.RewChoiceItemCount[rewardIndex], ct);

        // Убрать из журнала, пометить сданным.
        session.QuestSlots[slot] = null;
        session.CompletedQuests.Add(questId);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.QuestLogSlotId(slot), 0);
                m.SetUInt32(UpdateField.QuestLogSlotState(slot), 0);
                m.SetUInt32(UpdateField.QuestLogSlotCounters01(slot), 0);
                m.SetUInt32(UpdateField.QuestLogSlotCounters23(slot), 0);
            }), ct);
        await session.SendAsync(WorldOpcode.SmsgQuestgiverQuestComplete, QuestPackets.BuildQuestComplete(quest), ct);
        session.Logger.LogInformation("QUEST TURN-IN '{User}': quest={Quest} (деньги {Money})", session.Account, questId, quest.RewOrReqMoney);
    }

    /// <summary>Шлёт окно сдачи (SMSG_QUESTGIVER_OFFER_REWARD) с наградами квеста.</summary>
    private static async Task SendOfferRewardAsync(WorldSession session, ulong npcGuid, uint questId, CancellationToken ct)
    {
        var quest = await session.WorldDb.GetQuestAsync(questId, ct);
        if (quest is null)
            return;
        var rewardEntries = quest.RewItemId.Concat(quest.RewChoiceItemId).Where(id => id != 0).ToArray();
        IReadOnlyDictionary<uint, (uint DisplayId, byte InvType)> displays = rewardEntries.Length > 0
            ? await session.WorldDb.GetItemDisplaysAsync(rewardEntries, ct)
            : new Dictionary<uint, (uint, byte)>();
        await session.SendAsync(WorldOpcode.SmsgQuestgiverOfferReward,
            QuestPackets.BuildOfferReward(npcGuid, quest, displays), ct);
    }

    /// <summary>
    /// Засчитывает существо (убийство M6.3/M6.7 или разговор) в цели активных квестов: инкремент
    /// счётчиков, SMSG_QUESTUPDATE_ADD_KILL, при выполнении всех целей — SMSG_QUESTUPDATE_COMPLETE. M6.5.
    /// </summary>
    internal static async Task CreditCreatureAsync(WorldSession session, uint creatureEntry, ulong creatureGuid, CancellationToken ct)
    {
        for (var slot = 0; slot < session.QuestSlots.Length; slot++)
        {
            var p = session.QuestSlots[slot];
            if (p is null || p.Complete)
                continue;

            var changed = false;
            for (var i = 0; i < 4; i++)
                if (p.Creature[i] == (int)creatureEntry && p.Count[i] < p.Required[i])
                {
                    p.Count[i]++;
                    changed = true;
                    await session.SendAsync(WorldOpcode.SmsgQuestupdateAddKill,
                        QuestPackets.BuildAddKill(p.QuestId, creatureEntry, p.Count[i], p.Required[i], creatureGuid), ct);
                }
            if (!changed)
                continue;

            var c01 = (p.Count[0] & 0xFFFF) | ((p.Count[1] & 0xFFFF) << 16);
            var c23 = (p.Count[2] & 0xFFFF) | ((p.Count[3] & 0xFFFF) << 16);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
                {
                    m.SetUInt32(UpdateField.QuestLogSlotCounters01(slot), c01);
                    m.SetUInt32(UpdateField.QuestLogSlotCounters23(slot), c23);
                }), ct);

            if (!p.HasItemObjectives && p.CreatureObjectivesMet())
            {
                p.Complete = true;
                await session.SendAsync(WorldOpcode.SmsgQuestupdateComplete, QuestPackets.BuildUpdateComplete(p.QuestId), ct);
            }
        }
    }

    /// <summary>Грузит квест + displayId предметов-наград и шлёт SMSG_QUESTGIVER_QUEST_DETAILS.</summary>
    private static async Task SendQuestDetailsAsync(WorldSession session, ulong npcGuid, uint questId, CancellationToken ct)
    {
        var quest = await session.WorldDb.GetQuestAsync(questId, ct);
        if (quest is null)
            return;

        var rewardEntries = quest.RewItemId.Concat(quest.RewChoiceItemId).Where(id => id != 0).ToArray();
        IReadOnlyDictionary<uint, (uint DisplayId, byte InvType)> displays =
            rewardEntries.Length > 0
                ? await session.WorldDb.GetItemDisplaysAsync(rewardEntries, ct)
                : new Dictionary<uint, (uint, byte)>();

        await session.SendAsync(WorldOpcode.SmsgQuestgiverQuestDetails,
            QuestPackets.BuildQuestDetails(npcGuid, quest, displays), ct);
    }
}
