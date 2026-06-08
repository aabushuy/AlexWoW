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

        if (session.World.Quests.IsGiver(entry))
        {
            var quests = await session.WorldDb.GetGiverQuestsAsync(entry, ct);
            session.Logger.LogDebug("QUEST hello '{User}': npc={Entry}, {Count} квестов", session.Account, entry, quests.Count);
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

        // Уже в журнале? Иначе — в первый свободный слот PLAYER_QUEST_LOG.
        if (Array.IndexOf(session.QuestLog, questId) >= 0)
            return;
        var slot = Array.IndexOf(session.QuestLog, 0u);
        if (slot < 0)
            return; // журнал полон (SMSG_QUESTLOG_FULL — позже)

        session.QuestLog[slot] = questId;
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid,
                m => m.SetUInt32(UpdateField.QuestLogSlotId(slot), questId)), ct);
        session.Logger.LogInformation("QUEST ACCEPT '{User}': quest={Quest} → слот {Slot}", session.Account, questId, slot);
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
