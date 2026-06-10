using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Иконки квестгиверов «!»/«?» (M6.5/M6.10, DI-сервис M7 S5 — вынос из god-класса QuestHandlers):
/// расчёт статуса NPC и рассылка SMSG_QUESTGIVER_STATUS / SMSG_QUESTGIVER_STATUS_MULTIPLE.
/// </summary>
internal sealed class QuestGiverStatusService
{
    // QuestGiverStatus (3.3.5, сверено с TrinityCore QuestDef.h): NONE=0, INCOMPLETE=5 (серый «?»),
    // AVAILABLE=8 («!» жёлтый), REWARD=10 («?» жёлтый — можно сдать).
    private const byte StatusNone = 0;
    private const byte StatusIncomplete = 5;
    private const byte StatusAvailable = 8;
    private const byte StatusReward = 10;

    /// <summary>
    /// Статус иконки над существом (M6.10): приёмщик с выполненным квестом в журнале → «?» REWARD;
    /// дающий с доступным (берущимся) квестом → «!» AVAILABLE; приёмщик с квестом в процессе → серый «?»
    /// INCOMPLETE. Приоритет: сдача → взять → в процессе.
    /// </summary>
    internal async Task<byte> StatusForAsync(WorldSession session, uint entry, CancellationToken ct)
    {
        var quests = session.World.Quests;
        var hasIncomplete = false;
        if (quests.IsEnder(entry))
        {
            var enderIds = quests.EnderQuestIds(entry);
            foreach (var p in session.QuestSlots)
            {
                if (p is null || Array.IndexOf(enderIds, p.QuestId) < 0)
                    continue;
                if (p.Complete)
                    return StatusReward;   // можно сдать → жёлтый «?»
                hasIncomplete = true;      // взят, но не выполнен → серый «?»
            }
        }
        if (quests.IsGiver(entry))
        {
            var all = await session.WorldDb.GetGiverQuestsAsync(entry, ct);
            if (all.Any(q => QuestDialogService.CanTakeQuest(session, q)))
                return StatusAvailable;    // есть берущийся квест → «!»
        }
        return hasIncomplete ? StatusIncomplete : StatusNone;
    }

    /// <summary>
    /// Пушит АКТУАЛЬНЫЕ статусы иконок видимым квест-NPC одиночными SMSG_QUESTGIVER_STATUS (0x183) —
    /// после изменения состояния квестов (взятие/прогресс/сдача). Шлём по каждому giver/ender, ВКЛЮЧАЯ
    /// статус None (он очищает иконку у клиента), иначе исчезнувшая «!»/«?» не убирается. Multiple-ответ
    /// клиент шлёт лишь по своему запросу (или на релоге), поэтому для живого обновления нужен этот push. M7 #18.
    /// </summary>
    internal async Task PushQuestStatusesAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return;
        await session.World.Quests.EnsureLoadedAsync(ct);
        foreach (var (guid, creature) in session.VisibleNpcs)
        {
            var entry = creature.Template.Entry;
            if (!session.World.Quests.IsGiver(entry) && !session.World.Quests.IsEnder(entry))
                continue; // не квест-NPC — иконки и так нет
            var status = await StatusForAsync(session, entry, ct);
            await session.SendAsync(WorldOpcode.SmsgQuestgiverStatus,
                new ByteWriter(12).UInt64(guid).UInt32(status).ToArray(), ct);
        }
    }

    /// <summary>
    /// Шлёт статусы иконок (!/?) всех видимых квестгиверов одним SMSG_QUESTGIVER_STATUS_MULTIPLE —
    /// ОТВЕТ на запрос клиента (multiple-query) и на релоге; только не-None (клиент стартует без иконок). M6.5.
    /// </summary>
    internal async Task SendVisibleQuestStatusesAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return;
        await session.World.Quests.EnsureLoadedAsync(ct);

        var reports = new List<(ulong Guid, byte Status)>();
        foreach (var (guid, creature) in session.VisibleNpcs)
        {
            var status = await StatusForAsync(session, creature.Template.Entry, ct);
            if (status != StatusNone)
                reports.Add((guid, status));
        }

        var w = new ByteWriter(4 + reports.Count * 9).UInt32((uint)reports.Count);
        foreach (var (guid, status) in reports)
            w.UInt64(guid).UInt8(status);
        await session.SendAsync(WorldOpcode.SmsgQuestgiverStatusMultiple, w.ToArray(), ct);
    }
}
