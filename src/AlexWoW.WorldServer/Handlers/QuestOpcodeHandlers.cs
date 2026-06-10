using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Опкод-входы квестов (M6.5, DI-модуль M7 S5 — бывший god-класс QuestHandlers): тонкие методы чтения
/// пакета и делегирования. Логика разнесена по SRP: иконки «!»/«?» — <see cref="QuestGiverStatusService"/>,
/// прогресс/персист — <see cref="QuestProgressService"/>, диалоги/награды — <see cref="QuestDialogService"/>,
/// маршрутизация правого клика — <see cref="GossipService"/>.
/// </summary>
internal sealed class QuestOpcodeHandlers(
    GossipService gossip,
    QuestProgressService questProgress,
    QuestDialogService dialog,
    QuestGiverStatusService giverStatus) : IOpcodeHandlerModule
{
    /// <summary>entry шаблона существа из его GUID (0xF130 | entry&lt;&lt;24 | counter).</summary>
    private static uint CreatureEntry(ulong guid) => (uint)((guid >> 24) & 0xFFFFFF);

    [WorldOpcodeHandler(WorldOpcode.CmsgQuestgiverStatusQuery)]
    public async Task OnStatusQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        await session.World.Quests.EnsureLoadedAsync(ct);
        var guid = packet.Reader().UInt64();
        var status = await giverStatus.StatusForAsync(session, CreatureEntry(guid), ct);
        await session.SendAsync(WorldOpcode.SmsgQuestgiverStatus,
            new ByteWriter(12).UInt64(guid).UInt32(status).ToArray(), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgQuestgiverStatusMultipleQuery)]
    public Task OnStatusMultipleQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => giverStatus.SendVisibleQuestStatusesAsync(session, ct);

    [WorldOpcodeHandler(WorldOpcode.CmsgGossipHello, WorldOpcode.CmsgQuestgiverHello)]
    public Task OnHello(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => gossip.OnHelloAsync(session, packet.Reader().UInt64(), ct);

    [WorldOpcodeHandler(WorldOpcode.CmsgQuestgiverQueryQuest)]
    public async Task OnQueryQuest(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var npcGuid = r.UInt64();
        var questId = r.UInt32();
        await dialog.SendQuestDetailsAsync(session, npcGuid, questId, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgQuestgiverAcceptQuest)]
    public async Task OnAcceptQuest(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        r.UInt64();                       // npc guid
        var questId = r.UInt32();
        await questProgress.AcceptQuestAsync(session, questId, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgQuestQuery)]
    public async Task OnQuestQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var questId = packet.Reader().UInt32();
        var quest = await session.WorldDb.GetQuestAsync(questId, ct);
        if (quest is null)
            return;
        await session.SendAsync(WorldOpcode.SmsgQuestQueryResponse, QuestPackets.BuildQuestQueryResponse(quest), ct);
    }

    // CMSG_QUESTGIVER_COMPLETE_QUEST / REQUEST_REWARD (Guid + quest) → окно награды.
    [WorldOpcodeHandler(WorldOpcode.CmsgQuestgiverCompleteQuest, WorldOpcode.CmsgQuestgiverRequestReward)]
    public async Task OnRequestReward(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var npcGuid = r.UInt64();
        var questId = r.UInt32();
        await dialog.SendOfferRewardAsync(session, npcGuid, questId, ct);
    }

    // CMSG_QUESTGIVER_CHOOSE_REWARD (Guid + quest + reward index) → выдача награды + завершение.
    [WorldOpcodeHandler(WorldOpcode.CmsgQuestgiverChooseReward)]
    public async Task OnChooseReward(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        r.UInt64();                       // npc guid
        var questId = r.UInt32();
        var rewardIndex = r.UInt32();
        await dialog.ChooseRewardAsync(session, questId, rewardIndex, ct);
    }
}
