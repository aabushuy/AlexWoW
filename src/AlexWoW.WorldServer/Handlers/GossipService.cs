using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Оркестрация правого клика по NPC (M6.5/M9.3, DI-сервис M7 S5 — вынос из god-класса QuestHandlers):
/// маршрутизация госсипа «приёмщик квеста → квестгивер → тренер → вендор».
/// Тренер/вендор — пока легаси-статики (конверсия в S6).
/// </summary>
internal sealed class GossipService(QuestProgressService questProgress, QuestDialogService dialog)
{
    /// <summary>entry шаблона существа из его GUID (0xF130 | entry&lt;&lt;24 | counter).</summary>
    private static uint CreatureEntry(ulong guid) => (uint)((guid >> 24) & 0xFFFFFF);

    /// <summary>
    /// CMSG_GOSSIP_HELLO / CMSG_QUESTGIVER_HELLO (u64 npc): квестгивер → список/детали квестов;
    /// иначе — окно вендора (если NPC торгует). Объединено, т.к. на правый клик клиент шлёт GOSSIP_HELLO.
    /// </summary>
    internal async Task OnHelloAsync(WorldSession session, ulong npcGuid, CancellationToken ct)
    {
        await session.World.Quests.EnsureLoadedAsync(ct);
        var entry = CreatureEntry(npcGuid);

        // Разговор засчитывает цели «поговорить с этим NPC».
        await questProgress.CreditCreatureAsync(session, entry, npcGuid, ct);

        // Сдача: NPC принимает завершённый квест из журнала → окно награды.
        if (session.World.Quests.IsEnder(entry))
        {
            var enderIds = await session.WorldDb.GetEnderQuestIdsAsync(entry, ct);
            var done = Array.Find(session.QuestSlots, s => s is { Complete: true } p && enderIds.Contains(p.QuestId));
            if (done is not null)
            {
                await dialog.SendOfferRewardAsync(session, npcGuid, done.QuestId, ct);
                return;
            }
        }

        if (session.World.Quests.IsGiver(entry))
        {
            var all = await session.WorldDb.GetGiverQuestsAsync(entry, ct);
            var quests = all.Where(q => QuestDialogService.CanTakeQuest(session, q)).ToList(); // фильтр пригодности
            session.Logger.LogDebug("QUEST hello '{User}': npc={Entry}, {Take}/{All} берущихся",
                session.Account, entry, quests.Count, all.Count);
            if (quests.Count == 1)
            {
                await dialog.SendQuestDetailsAsync(session, npcGuid, quests[0].QuestId, ct);
                return;
            }
            if (quests.Count > 1)
            {
                await session.SendAsync(WorldOpcode.SmsgQuestgiverQuestList,
                    QuestPackets.BuildQuestList(npcGuid, string.Empty, quests), ct);
                return;
            }
        }

        // Тренер класса (M9.3): если NPC — тренер, подходящий игроку, открыть меню госсипа с пунктом
        // «обучиться» (приоритет над вендором — классовые тренеры обычно не торгуют). У тренеров флаг
        // GOSSIP → клиент ждёт меню, прямой SMSG_TRAINER_LIST игнорирует; список — на выбор пункта.
        // Легаси-статики (конверсия в S6) — инстанс-сервису звать их можно.
        if (TrainerHandlers.IsTrainerNpc(session, npcGuid)
            && await TrainerHandlers.TrySendTrainerGossipAsync(session, npcGuid, ct))
            return;

        // Не квестгивер/тренер (или без доступных квестов) — попробуем как вендора.
        await VendorHandlers.SendVendorListAsync(session, npcGuid, ct);
    }
}
