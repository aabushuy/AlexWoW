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
    // QuestGiverStatus (3.3.5, сверено с TrinityCore QuestDef.h): NONE=0, INCOMPLETE=5 (серый «?»),
    // AVAILABLE=8 («!» жёлтый), REWARD=10 («?» жёлтый — можно сдать).
    private const byte StatusNone = 0;
    private const byte StatusIncomplete = 5;
    private const byte StatusAvailable = 8;
    private const byte StatusReward = 10;

    // M6.10: статус строки character_queststatus. 0 = активен (в журнале), 1 = сдан.
    private const byte QuestStatusActive = 0;
    private const byte QuestStatusRewarded = 1;

    /// <summary>Сохраняет активный квест (id/слот/счётчики) в character_queststatus. M6.10.</summary>
    private static Task PersistActiveAsync(WorldSession session, int slot, World.QuestProgress p, CancellationToken ct)
        => session.Characters.UpsertQuestStatusAsync(session.InWorldGuid, p.QuestId, (byte)slot, QuestStatusActive,
            (ushort)p.Count[0], (ushort)p.Count[1], (ushort)p.Count[2], (ushort)p.Count[3], ct);

    /// <summary>
    /// Нужен ли игроку этот предмет для активного квеста (квест-дроп с трупа, M6.10): есть незавершённый
    /// квест с item-целью на <paramref name="itemId"/>, по которой ещё не набрано нужное количество.
    /// </summary>
    internal static bool NeedsQuestItem(WorldSession session, uint itemId)
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
    /// Списывает <paramref name="count"/> предметов entry из сумок (квест-предметы при сдаче, M6.10):
    /// целые предметы удаляет (DestroyObject + очистка слота), частичную стопку уменьшает
    /// (ITEM_FIELD_STACK_COUNT), с персистом. Освобождает слоты под награду.
    /// </summary>
    private static async Task ConsumeItemsAsync(WorldSession session, uint itemEntry, uint count, CancellationToken ct)
    {
        var ownerGuid = session.InWorldGuid;
        var remaining = count;
        foreach (var item in session.Inventory.Where(i => i.ItemEntry == itemEntry).ToList())
        {
            if (remaining == 0)
                break;
            if (item.StackCount <= remaining)
            {
                remaining -= item.StackCount;
                session.Inventory.Remove(item);
                await session.Characters.RemoveItemAsync(item.ItemGuid, ct);
                await session.SendAsync(WorldOpcode.SmsgDestroyObject,
                    new ByteWriter(9).UInt64(ItemObject.ItemGuid(item.ItemGuid)).UInt8(0).ToArray(), ct);
                if (item.Bag == InventorySlots.MainBag)
                    await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                        PlayerSpawn.BuildInvSlotUpdate(ownerGuid, item.Slot, 0), ct);
            }
            else
            {
                item.StackCount -= remaining;
                remaining = 0;
                await session.Characters.SetItemStackAsync(item.ItemGuid, item.StackCount, ct);
                await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                    ItemObject.BuildStackUpdate(ItemObject.ItemGuid(item.ItemGuid), item.StackCount), ct);
            }
        }
    }

    /// <summary>Сколько предметов entry в сумках игрока (для item-целей). M6.10.</summary>
    private static uint CountItem(WorldSession session, uint itemEntry)
    {
        uint total = 0;
        foreach (var it in session.Inventory)
            if (it.ItemEntry == itemEntry)
                total += it.StackCount;
        return total;
    }

    /// <summary>
    /// Зачёт получения предмета (лут/выдача) в item-цели квестов: если новый предмет завершил квест —
    /// помечаем выполненным (SMSG_QUESTUPDATE_COMPLETE + персист). Прогресс-счётчик клиент рисует сам
    /// по сумкам. Зовётся из InventoryGrant.TryGiveAsync. M6.10.
    /// </summary>
    internal static async Task OnItemGainedAsync(WorldSession session, uint itemEntry, CancellationToken ct)
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
        }
    }

    /// <summary>entry шаблона существа из его GUID (0xF130 | entry&lt;&lt;24 | counter).</summary>
    private static uint CreatureEntry(ulong guid) => (uint)((guid >> 24) & 0xFFFFFF);

    /// <summary>
    /// Статус иконки над существом (M6.10): приёмщик с выполненным квестом в журнале → «?» REWARD;
    /// дающий с доступным (берущимся) квестом → «!» AVAILABLE; приёмщик с квестом в процессе → серый «?»
    /// INCOMPLETE. Приоритет: сдача → взять → в процессе.
    /// </summary>
    private static async Task<byte> StatusForAsync(WorldSession session, uint entry, CancellationToken ct)
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
            if (all.Any(q => CanTakeQuest(session, q)))
                return StatusAvailable;    // есть берущийся квест → «!»
        }
        return hasIncomplete ? StatusIncomplete : StatusNone;
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgQuestgiverStatusQuery)]
    public static async Task OnStatusQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        await session.World.Quests.EnsureLoadedAsync(ct);
        var guid = packet.Reader().UInt64();
        var status = await StatusForAsync(session, CreatureEntry(guid), ct);
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
            var status = await StatusForAsync(session, creature.Template.Entry, ct);
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

        // Тренер класса (M9.3): если NPC — тренер, подходящий игроку, открыть меню госсипа с пунктом
        // «обучиться» (приоритет над вендором — классовые тренеры обычно не торгуют). У тренеров флаг
        // GOSSIP → клиент ждёт меню, прямой SMSG_TRAINER_LIST игнорирует; список — на выбор пункта.
        if (TrainerHandlers.IsTrainerNpc(session, npcGuid)
            && await TrainerHandlers.TrySendTrainerGossipAsync(session, npcGuid, ct))
            return;

        // Не квестгивер/тренер (или без доступных квестов) — попробуем как вендора.
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

        // M6.10: списать квест-предметы цели (освобождает слоты под предметы-награды).
        for (var i = 0; i < 4; i++)
            if (quest.ReqItemId[i] != 0 && quest.ReqItemCount[i] > 0)
                await ConsumeItemsAsync(session, quest.ReqItemId[i], quest.ReqItemCount[i], ct);

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
        await session.Characters.UpsertQuestStatusAsync(session.InWorldGuid, questId, 0, QuestStatusRewarded,
            0, 0, 0, 0, ct); // M6.10: персист сдачи
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
            await PersistActiveAsync(session, slot, p, ct); // M6.10: персист прогресса

            if (!p.Complete && p.ObjectivesMet(e => CountItem(session, e)))
            {
                p.Complete = true;
                await session.SendAsync(WorldOpcode.SmsgQuestupdateComplete, QuestPackets.BuildUpdateComplete(p.QuestId), ct);
            }
        }
    }

    /// <summary>
    /// Восстанавливает СОСТОЯНИЕ квестов персонажа из БД (M6.10): сданные → CompletedQuests; активные →
    /// QuestProgress в журнал (счётчики из БД, Complete рекомпьютится из quest_template + инвентаря).
    /// Без клиентских пакетов — поля журнала кладутся в НАЧАЛЬНЫЙ спавн (PlayerSpawn), иначе клиент
    /// принимает их за новое взятие квеста (звук + «Получено задание»). Зовётся ДО спавна в OnPlayerLogin.
    /// </summary>
    internal static async Task LoadQuestStateAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return;
        var rows = await session.Characters.GetQuestStatusesAsync(session.InWorldGuid, ct);
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
