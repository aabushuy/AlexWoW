using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Прогресс и персист квестов (M6.5/M6.10, DI-сервис M7 S5 — вынос из god-класса QuestHandlers):
/// зачёт убийств/разговоров и item-целей, взятие квеста, восстановление состояния из БД при входе,
/// строки character_queststatus. Иконки квестгиверов — <see cref="QuestGiverStatusService"/>,
/// диалоги/награды — <see cref="QuestDialogService"/>.
/// </summary>
internal sealed class QuestProgressService(QuestGiverStatusService giverStatus)
{
    // M6.10: статус строки character_queststatus. 0 = активен (в журнале), 1 = сдан.
    private const byte QuestStatusActive = 0;
    private const byte QuestStatusRewarded = 1;

    /// <summary>Сохраняет активный квест (id/слот/счётчики) в character_queststatus. M6.10.</summary>
    private static Task PersistActiveAsync(WorldSession session, int slot, World.QuestProgress p, CancellationToken ct)
        => session.Quests.UpsertQuestStatusAsync(session.InWorldGuid, p.QuestId, (byte)slot, QuestStatusActive,
            (ushort)p.Count[0], (ushort)p.Count[1], (ushort)p.Count[2], (ushort)p.Count[3], ct);

    /// <summary>Помечает квест сданным в character_queststatus (сдача награды, M6.10). Константа статуса
    /// живёт здесь — персист квестов целиком в этом сервисе.</summary>
    internal Task MarkRewardedAsync(WorldSession session, uint questId, CancellationToken ct)
        => session.Quests.UpsertQuestStatusAsync(session.InWorldGuid, questId, 0, QuestStatusRewarded,
            0, 0, 0, 0, ct);

    /// <summary>Сколько предметов entry в сумках игрока (для item-целей). M6.10.</summary>
    private static uint CountItem(WorldSession session, uint itemEntry)
        => InventoryGrantService.CountItem(session, itemEntry);

    /// <summary>
    /// Нужен ли игроку этот предмет для активного квеста (квест-дроп с трупа, M6.10): есть незавершённый
    /// квест с item-целью на <paramref name="itemId"/>, по которой ещё не набрано нужное количество.
    /// </summary>
    internal bool NeedsQuestItem(WorldSession session, uint itemId)
    {
        foreach (var p in session.QuestSlots)
        {
            if (p is null || p.Complete)
                continue;
            for (var i = 0; i < 4; i++)
                if (p.ReqItem[i] == itemId && CountItem(session, itemId) < p.ReqItemCount[i])
                    return true;
        }
        return false;
    }

    /// <summary>
    /// Зачёт получения предмета (лут/выдача) в item-цели квестов: если новый предмет завершил квест —
    /// помечаем выполненным (SMSG_QUESTUPDATE_COMPLETE + персист). Прогресс-счётчик клиент рисует сам
    /// по сумкам. Зовётся из InventoryGrantService.TryGiveAsync. M6.10.
    /// </summary>
    internal async Task OnItemGainedAsync(WorldSession session, uint itemEntry, CancellationToken ct)
    {
        for (var slot = 0; slot < session.QuestSlots.Length; slot++)
        {
            var p = session.QuestSlots[slot];
            if (p is null || p.Complete || Array.IndexOf(p.ReqItem, itemEntry) < 0)
                continue;
            if (!p.ObjectivesMet(e => CountItem(session, e)))
                continue;
            p.Complete = true;
            await session.SendAsync(WorldOpcode.SmsgQuestupdateComplete, QuestPackets.BuildUpdateComplete(p.QuestId), ct);
            await PersistActiveAsync(session, slot, p, ct);
            await giverStatus.PushQuestStatusesAsync(session, ct); // M7 #18: «?» появляется у приёмщика
        }
    }

    /// <summary>
    /// Взятие квеста (CMSG_QUESTGIVER_ACCEPT_QUEST): свободный слот журнала → QuestProgress (цели из
    /// quest_template) → персист → поле журнала клиенту. Квест без целей сразу выполнен. M6.5.
    /// </summary>
    internal async Task AcceptQuestAsync(WorldSession session, uint questId, CancellationToken ct)
    {
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
            prog.ReqItem[i] = quest.ReqItemId[i];
            prog.ReqItemCount[i] = quest.ReqItemCount[i];
        }
        prog.Complete = prog.ObjectivesMet(e => CountItem(session, e)); // M6.10: учёт item-целей по инвентарю
        session.QuestSlots[slot] = prog;
        await PersistActiveAsync(session, slot, prog, ct); // M6.10: персист

        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid,
                m => m.SetUInt32(UpdateField.QuestLogSlotId(slot), questId)), ct);
        if (prog.Complete) // квест без целей (напр. «поговорить с X», где X — приёмщик) сразу выполнен
            await session.SendAsync(WorldOpcode.SmsgQuestupdateComplete, QuestPackets.BuildUpdateComplete(questId), ct);
        await giverStatus.PushQuestStatusesAsync(session, ct); // M7 #18: «!» сразу пропадает после взятия
        session.Logger.LogInformation("QUEST ACCEPT '{User}': quest={Quest} → слот {Slot} (complete={C})",
            session.Account, questId, slot, prog.Complete);
    }

    /// <summary>
    /// Засчитывает существо (убийство M6.3/M6.7 или разговор) в цели активных квестов: инкремент
    /// счётчиков, SMSG_QUESTUPDATE_ADD_KILL, при выполнении всех целей — SMSG_QUESTUPDATE_COMPLETE. M6.5.
    /// </summary>
    internal async Task CreditCreatureAsync(WorldSession session, uint creatureEntry, ulong creatureGuid, CancellationToken ct)
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
            await PersistActiveAsync(session, slot, p, ct); // M6.10: персист прогресса

            if (!p.Complete && p.ObjectivesMet(e => CountItem(session, e)))
            {
                p.Complete = true;
                await session.SendAsync(WorldOpcode.SmsgQuestupdateComplete, QuestPackets.BuildUpdateComplete(p.QuestId), ct);
                await giverStatus.PushQuestStatusesAsync(session, ct); // M7 #18: «?» появляется у приёмщика
            }
        }
    }

    /// <summary>
    /// Восстанавливает СОСТОЯНИЕ квестов персонажа из БД (M6.10): сданные → CompletedQuests; активные →
    /// QuestProgress в журнал (счётчики из БД, Complete рекомпьютится из quest_template + инвентаря).
    /// Без клиентских пакетов — поля журнала кладутся в НАЧАЛЬНЫЙ спавн (PlayerSpawn), иначе клиент
    /// принимает их за новое взятие квеста (звук + «Получено задание»). Зовётся ДО спавна в OnPlayerLogin.
    /// </summary>
    internal async Task LoadQuestStateAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return;
        var rows = await session.Quests.GetQuestStatusesAsync(session.InWorldGuid, ct);
        foreach (var row in rows)
        {
            if (row.Status == QuestStatusRewarded)
            {
                session.CompletedQuests.Add(row.QuestId);
                continue;
            }
            if (row.Slot >= session.QuestSlots.Length)
                continue;
            var quest = await session.WorldDb.GetQuestAsync(row.QuestId, ct);
            if (quest is null)
                continue;

            var prog = new World.QuestProgress
            {
                QuestId = row.QuestId,
                HasItemObjectives = quest.ReqItemId.Any(id => id != 0),
            };
            for (var i = 0; i < 4; i++)
            {
                prog.Creature[i] = quest.ReqCreatureOrGoId[i];
                prog.Required[i] = quest.ReqCreatureOrGoCount[i];
                prog.ReqItem[i] = quest.ReqItemId[i];
                prog.ReqItemCount[i] = quest.ReqItemCount[i];
            }
            prog.Count[0] = row.Counter0; prog.Count[1] = row.Counter1;
            prog.Count[2] = row.Counter2; prog.Count[3] = row.Counter3;
            prog.Complete = prog.ObjectivesMet(e => CountItem(session, e)); // M6.10: + item-цели
            session.QuestSlots[row.Slot] = prog;
        }
        session.Logger.LogInformation("QUEST LOAD '{User}': {Active} активных, {Done} сданных",
            session.Account, session.QuestSlots.Count(s => s is not null), session.CompletedQuests.Count);
    }
}
