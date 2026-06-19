// Порт CMaNGOS-WoTLK: src/game/Groups/GroupHandler.cpp (HandleGroup*Opcode)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Groups/GroupHandler.cpp. GPL-2.0.

using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Group;

/// <summary>
/// Опкод-модуль группы/партии (GROUP.T1): invite/accept/decline + базовый kick by leader.
/// </summary>
/// <remarks>
/// Что покрыто: CMSG_GROUP_INVITE, CMSG_GROUP_ACCEPT, CMSG_GROUP_DECLINE, CMSG_GROUP_UNINVITE_GUID.
/// Тесты на CMaNGOS не дают пройти, если попытка пригласить себя/уже-приглашённого/мертвого.
/// SMSG_GROUP_LIST sync — в T2; смена лидера/disband — в T3; XP/loot — в T4; raid — в T5.
/// </remarks>
internal sealed class GroupHandlers(GroupRegistry registry, GroupSyncService sync,
    GroupPersistenceService persist) : IOpcodeHandlerModule
{
    [WorldOpcodeHandler(WorldOpcode.CmsgGroupInvite)]
    public async Task OnGroupInvite(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var membername = reader.CString();
        // proposedRoles (uint32, WotLK LFG-расширение) — игнорируем в T1.

        if (string.IsNullOrWhiteSpace(membername))
        {
            await SendPartyResultAsync(session, GroupPackets.PartyOperation.Invite, membername,
                GroupPackets.PartyResult.BadPlayerNameS, ct);
            return;
        }

        var initiatorGuid = (ulong)session.InWorldGuid;
        var recipient = FindOnlinePlayerByName(session, membername);
        // Нельзя пригласить себя / несуществующего.
        if (recipient is null || recipient.Guid == initiatorGuid)
        {
            await SendPartyResultAsync(session, GroupPackets.PartyOperation.Invite, membername,
                GroupPackets.PartyResult.BadPlayerNameS, ct);
            return;
        }

        var recipientGroup = registry.GetByChar(recipient.Guid);
        // Уже в другой группе/инвайте.
        if (recipientGroup is not null)
        {
            await SendPartyResultAsync(session, GroupPackets.PartyOperation.Invite, membername,
                GroupPackets.PartyResult.AlreadyInGroupS, ct);
            // Inform invitee (флаг alreadyInGroup) — клиент покажет тост «уже в группе».
            await recipient.Session.SendAsync(WorldOpcode.SmsgGroupInvite,
                GroupPackets.BuildGroupInvite(session.Character?.Name ?? "", alreadyInGroup: true), ct);
            return;
        }

        var initiatorGroup = registry.GetByChar(initiatorGuid);

        if (initiatorGroup is not null)
        {
            // Без permissions (не лидер и не ассистент) — отклоняем (полные permissions в T3/T5).
            if (!initiatorGroup.IsLeader(initiatorGuid))
            {
                await SendPartyResultAsync(session, GroupPackets.PartyOperation.Invite, "",
                    GroupPackets.PartyResult.NotLeader, ct);
                return;
            }
            if (initiatorGroup.IsFull)
            {
                await SendPartyResultAsync(session, GroupPackets.PartyOperation.Invite, "",
                    GroupPackets.PartyResult.GroupFull, ct);
                return;
            }
            if (!initiatorGroup.AddInvite(recipient.Guid))
                return;
            registry.TrackInvite(initiatorGroup, recipient.Guid);
        }
        else
        {
            // Группы ещё нет — создаём invite-only (закоммитится в T2 при первом accept).
            var leaderName = session.Character?.Name ?? "";
            var g = registry.CreateInviteOnly(initiatorGuid, leaderName);
            if (!g.AddInvite(recipient.Guid))
            {
                registry.Remove(g);
                return;
            }
            registry.TrackInvite(g, recipient.Guid);
        }

        // OK инициатору + приглашение получателю.
        await SendPartyResultAsync(session, GroupPackets.PartyOperation.Invite, membername,
            GroupPackets.PartyResult.Ok, ct);
        await recipient.Session.SendAsync(WorldOpcode.SmsgGroupInvite,
            GroupPackets.BuildGroupInvite(session.Character?.Name ?? "", alreadyInGroup: false), ct);

        session.Logger.LogInformation("GROUP '{User}' invite -> '{Name}'", session.Account, membername);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGroupAccept)]
    public async Task OnGroupAccept(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        _ = packet; // proposedRoles (uint32) — игнорируем в T1
        var charGuid = (ulong)session.InWorldGuid;
        var group = registry.GetByChar(charGuid);
        if (group is null || !group.HasInvite(charGuid))
            return; // нет активного приглашения
        if (group.LeaderGuid == charGuid)
        {
            session.Logger.LogWarning("GROUP '{User}' accept-у собственного invite — игнорируем", session.Account);
            return;
        }

        // Лимит мог появиться, пока он думал.
        if (group.IsFull)
        {
            await SendPartyResultAsync(session, GroupPackets.PartyOperation.Invite, "",
                GroupPackets.PartyResult.GroupFull, ct);
            registry.DetachChar(charGuid);
            group.RemoveInvite(charGuid);
            return;
        }

        // Первая инициация: при первом accept'е invite-only → Created, лидер становится первым членом.
        if (!group.IsCreated)
        {
            group.AddMember(group.LeaderGuid, group.LeaderName);
            registry.OnMemberJoined(group, group.LeaderGuid);
        }

        var myName = session.Character?.Name ?? "";
        if (!group.AddMember(charGuid, myName))
            return;
        registry.OnMemberJoined(group, charGuid);

        session.Logger.LogInformation("GROUP '{User}' принял приглашение в группу {Group} (членов {N})",
            session.Account, group.Id, group.MemberCount);

        // T6: persistence — первый accept делает Group Created. Сохраняем заголовок + лидера + нового.
        if (group.PersistedId == 0)
        {
            await persist.SaveNewGroupAsync(group, ct);
            var leaderMember = group.Members.FirstOrDefault(m => m.Guid == group.LeaderGuid);
            if (leaderMember is not null)
                await persist.SaveMemberAsync(group, leaderMember, ct);
        }
        var newMember = group.Members.FirstOrDefault(m => m.Guid == charGuid);
        if (newMember is not null)
            await persist.SaveMemberAsync(group, newMember, ct);

        // T2: SMSG_GROUP_LIST + PARTY_MEMBER_STATS всем членам — клиенты видят панель партии.
        await sync.SendGroupListAsync(group, session.World, ct);
        await sync.SendAllStatsAsync(group, session.World, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGroupDecline)]
    public async Task OnGroupDecline(WorldSession session, IncomingPacket _, CancellationToken ct)
    {
        var charGuid = (ulong)session.InWorldGuid;
        var group = registry.GetByChar(charGuid);
        if (group is null || !group.HasInvite(charGuid))
            return;

        group.RemoveInvite(charGuid);
        registry.DetachChar(charGuid);

        // Уведомление инициатора.
        var leader = group.LeaderGuid != 0 ? session.World.FindPlayer(group.LeaderGuid) : null;
        if (leader is not null)
        {
            var myName = session.Character?.Name ?? "";
            await leader.Session.SendAsync(WorldOpcode.SmsgGroupDecline,
                GroupPackets.BuildGroupDecline(myName), ct);
        }

        // Если invite-only группа без других приглашённых и без членов — сносим.
        if (!group.IsCreated && group.Invites.Count == 0)
            registry.Remove(group);

        session.Logger.LogInformation("GROUP '{User}' declined invite (group {Group})", session.Account, group.Id);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGroupUninviteGuid)]
    public async Task OnGroupUninviteGuid(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var targetGuid = reader.UInt64();
        // CString reason — игнорируем (часть протокола, не используется CMaNGOS).

        var initiatorGuid = (ulong)session.InWorldGuid;
        var group = registry.GetByChar(initiatorGuid);
        if (group is null || !group.IsLeader(initiatorGuid))
        {
            await SendPartyResultAsync(session, GroupPackets.PartyOperation.UninviteOrLeave, "",
                GroupPackets.PartyResult.NotLeader, ct);
            return;
        }
        if (targetGuid == initiatorGuid)
            return; // лидер не выкидывает самого себя через uninvite (для этого CMSG_GROUP_DISBAND)

        if (!group.ContainsMember(targetGuid))
        {
            await SendPartyResultAsync(session, GroupPackets.PartyOperation.UninviteOrLeave, "",
                GroupPackets.PartyResult.TargetNotInGroupS, ct);
            return;
        }

        group.RemoveMember(targetGuid);
        registry.DetachChar(targetGuid);
        await persist.DeleteMemberAsync(group, targetGuid, ct); // T6
        session.Logger.LogInformation("GROUP '{User}' выкинул {Guid} из группы {Group}",
            session.Account, targetGuid, group.Id);

        // T2: пересинхронизировать оставшимся; выкинутому послать «вы покинули» — отдельно T3.
        await sync.SendGroupListAsync(group, session.World, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGroupSetLeader)]
    public async Task OnGroupSetLeader(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var newLeaderGuid = reader.UInt64();

        var initiatorGuid = (ulong)session.InWorldGuid;
        var group = registry.GetByChar(initiatorGuid);
        if (group is null || !group.IsLeader(initiatorGuid))
            return; // не лидер / не в группе — молча игнор (как CMaNGOS)
        if (!group.ContainsMember(newLeaderGuid))
            return; // нельзя сделать лидером не-члена

        var newName = group.ChangeLeader(newLeaderGuid);
        if (newName is null)
            return;
        await persist.UpdateGroupAsync(group, ct); // T6

        // Broadcast SMSG_GROUP_SET_LEADER + пересинхрон состава (новая шапка для каждого).
        var pkt = GroupPackets.BuildGroupSetLeader(newName);
        foreach (var m in group.Members)
        {
            if (!m.IsOnline)
                continue;
            var p = session.World.FindPlayer(m.Guid);
            if (p is null)
                continue;
            await p.Session.SendAsync(WorldOpcode.SmsgGroupSetLeader, pkt, ct);
        }
        await sync.SendGroupListAsync(group, session.World, ct);

        session.Logger.LogInformation("GROUP '{User}' назначил лидером {Name} (group {Id})",
            session.Account, newName, group.Id);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGroupDisband)]
    public async Task OnGroupDisband(WorldSession session, IncomingPacket _, CancellationToken ct)
    {
        var charGuid = (ulong)session.InWorldGuid;
        var group = registry.GetByChar(charGuid);
        if (group is null)
            return;

        // CMSG_GROUP_DISBAND — это «уйти из группы». Если лидер — распускаем всю; иначе только себя.
        if (group.IsLeader(charGuid))
        {
            await DisbandAsync(group, session.World, ct);
            session.Logger.LogInformation("GROUP '{User}' распустил группу {Id}", session.Account, group.Id);
        }
        else
        {
            await LeaveGroupAsync(group, charGuid, session.World, ct);
            session.Logger.LogInformation("GROUP '{User}' покинул группу {Id}", session.Account, group.Id);
        }
    }

    /// <summary>T3: полный disband — SMSG_GROUP_DESTROYED + empty SMSG_GROUP_LIST каждому, registry.Remove.</summary>
    internal async Task DisbandAsync(World.Group group, WorldState world, CancellationToken ct)
    {
        var destroyed = GroupPackets.BuildGroupDestroyed();
        var empty = GroupPackets.BuildEmptyGroupList();
        foreach (var m in group.Members.ToList())
        {
            if (m.IsOnline)
            {
                var p = world.FindPlayer(m.Guid);
                if (p is not null)
                {
                    await p.Session.SendAsync(WorldOpcode.SmsgGroupDestroyed, destroyed, ct);
                    await p.Session.SendAsync(WorldOpcode.SmsgGroupList, empty, ct);
                }
            }
            registry.DetachChar(m.Guid);
        }
        foreach (var iv in group.Invites.ToList())
            registry.DetachChar(iv);
        await persist.DeleteGroupAsync(group, ct); // T6
        registry.Remove(group);
    }

    /// <summary>
    /// T3: один уходит. Если осталось ≤1 — auto-disband. Если уходил лидер — promote первого онлайн.
    /// </summary>
    internal async Task LeaveGroupAsync(World.Group group, ulong charGuid, WorldState world, CancellationToken ct)
    {
        var wasLeader = group.IsLeader(charGuid);
        group.RemoveMember(charGuid);
        registry.DetachChar(charGuid);
        await persist.DeleteMemberAsync(group, charGuid, ct); // T6

        // Уведомить ушедшего, если он онлайн (empty list — клиент скроет UI партии).
        var leaver = world.FindPlayer(charGuid);
        if (leaver is not null)
        {
            await leaver.Session.SendAsync(WorldOpcode.SmsgGroupList, GroupPackets.BuildEmptyGroupList(), ct);
        }

        // Если осталось ≤ 1 — auto-disband (одиночка не группа).
        if (group.MemberCount <= 1)
        {
            await DisbandAsync(group, world, ct);
            return;
        }

        if (wasLeader)
        {
            var heir = group.PickOnlineHeirExceptLeader() ?? group.Members.FirstOrDefault();
            if (heir is not null && group.ChangeLeader(heir.Guid) is { } heirName)
            {
                await persist.UpdateGroupAsync(group, ct); // T6: persist нового лидера
                var pkt = GroupPackets.BuildGroupSetLeader(heirName);
                foreach (var m in group.Members)
                {
                    if (!m.IsOnline)
                        continue;
                    var p = world.FindPlayer(m.Guid);
                    if (p is not null)
                        await p.Session.SendAsync(WorldOpcode.SmsgGroupSetLeader, pkt, ct);
                }
            }
        }

        await sync.SendGroupListAsync(group, world, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgLootMethod)]
    public async Task OnLootMethod(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var lootMethod = reader.UInt32();
        var lootMaster = reader.UInt64();
        var lootThreshold = reader.UInt32();
        _ = lootThreshold; // T4 — храним только method + masterGuid; threshold через GROUP_LIST если > 1

        var initiatorGuid = (ulong)session.InWorldGuid;
        var group = registry.GetByChar(initiatorGuid);
        if (group is null || !group.IsLeader(initiatorGuid))
            return;
        // Валидное значение: 0-3 (FFA/RR/Master/Group).
        if (lootMethod > 3)
            return;

        group.LootMethod = (byte)lootMethod;
        group.LootMasterGuid = lootMaster;
        await persist.UpdateGroupAsync(group, ct); // T6
        await sync.SendGroupListAsync(group, session.World, ct);

        session.Logger.LogInformation("GROUP '{User}' loot method → {Method} (master {Master})",
            session.Account, lootMethod, lootMaster);
    }

    // ============================================================
    // GROUP.T5: raid / sub-groups / assistant / ready check / target icons
    // ============================================================

    [WorldOpcodeHandler(WorldOpcode.CmsgGroupRaidConvert)]
    public async Task OnGroupRaidConvert(WorldSession session, IncomingPacket _, CancellationToken ct)
    {
        var charGuid = (ulong)session.InWorldGuid;
        var group = registry.GetByChar(charGuid);
        if (group is null || !group.IsLeader(charGuid))
            return;
        if (group.Type == World.GroupType.Raid)
            return; // уже рейд

        group.Type = World.GroupType.Raid;
        await persist.UpdateGroupAsync(group, ct); // T6
        await sync.SendGroupListAsync(group, session.World, ct);
        session.Logger.LogInformation("GROUP '{User}' party → RAID (group {Id})", session.Account, group.Id);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGroupChangeSubGroup)]
    public async Task OnGroupChangeSubGroup(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var targetName = reader.CString();
        var newGroupIdx = reader.UInt8();

        var initiatorGuid = (ulong)session.InWorldGuid;
        var group = registry.GetByChar(initiatorGuid);
        if (group is null)
            return;
        // По CMaNGOS: лидер ИЛИ ассистент могут менять subgroup'ы. Sub-groups валидны 0..7.
        var initiator = group.Members.FirstOrDefault(m => m.Guid == initiatorGuid);
        if (initiator is null || (!group.IsLeader(initiatorGuid) && !initiator.IsAssistant))
            return;
        if (newGroupIdx > 7)
            return;

        var target = group.Members.FirstOrDefault(m =>
            string.Equals(m.Name, targetName, System.StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return;

        target.SubGroup = newGroupIdx;
        await persist.UpdateMemberAsync(group, target, ct); // T6
        await sync.SendGroupListAsync(group, session.World, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGroupAssistantLeader)]
    public async Task OnGroupAssistantLeader(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var targetGuid = reader.UInt64();
        var apply = reader.UInt8() != 0;

        var charGuid = (ulong)session.InWorldGuid;
        var group = registry.GetByChar(charGuid);
        if (group is null || !group.IsLeader(charGuid))
            return;

        var target = group.Members.FirstOrDefault(m => m.Guid == targetGuid);
        if (target is null)
            return;
        target.IsAssistant = apply;
        await persist.UpdateMemberAsync(group, target, ct); // T6
        await sync.SendGroupListAsync(group, session.World, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.MsgRaidReadyCheck)]
    public async Task OnRaidReadyCheck(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var charGuid = (ulong)session.InWorldGuid;
        var group = registry.GetByChar(charGuid);
        if (group is null)
            return;

        var initiator = group.Members.FirstOrDefault(m => m.Guid == charGuid);
        if (initiator is null)
            return;

        // CMaNGOS: ready check может стартовать лидер ИЛИ ассистент.
        var startedReadyCheck = !group.IsLeader(charGuid) ? initiator.IsAssistant : true;

        var reader = packet.Reader();
        // Если payload пустой — это «лидер начал ready check». Иначе — клиент отвечает (статус).
        if (reader.Remaining == 0)
        {
            if (!startedReadyCheck)
                return;
            // Broadcast всем в группе: MSG_RAID_READY_CHECK + senderGuid (8) — клиент откроет диалог.
            var pkt = new AlexWoW.Common.Network.ByteWriter(8).UInt64(charGuid).ToArray();
            foreach (var m in group.Members)
            {
                if (!m.IsOnline)
                    continue;
                var p = session.World.FindPlayer(m.Guid);
                if (p is not null)
                    await p.Session.SendAsync(WorldOpcode.MsgRaidReadyCheck, pkt, ct);
            }
            session.Logger.LogInformation("GROUP ready check started by '{User}' (group {Id})",
                session.Account, group.Id);
            return;
        }

        // Иначе клиент шлёт свой ответ (uint8 status: 0/1). Перенаправляем всем — клиент сам агрегирует.
        var status = reader.UInt8();
        var ack = new AlexWoW.Common.Network.ByteWriter(9).UInt64(charGuid).UInt8(status).ToArray();
        foreach (var m in group.Members)
        {
            if (!m.IsOnline)
                continue;
            var p = session.World.FindPlayer(m.Guid);
            if (p is not null)
                await p.Session.SendAsync(WorldOpcode.MsgRaidReadyCheckConfirm, ack, ct);
        }
    }

    [WorldOpcodeHandler(WorldOpcode.MsgRaidReadyCheckFinished)]
    public async Task OnRaidReadyCheckFinished(WorldSession session, IncomingPacket _, CancellationToken ct)
    {
        var charGuid = (ulong)session.InWorldGuid;
        var group = registry.GetByChar(charGuid);
        if (group is null || !group.IsLeader(charGuid))
            return;
        // Транслируем «завершено» всем (пустое тело).
        foreach (var m in group.Members)
        {
            if (!m.IsOnline)
                continue;
            var p = session.World.FindPlayer(m.Guid);
            if (p is not null)
                await p.Session.SendAsync(WorldOpcode.MsgRaidReadyCheckFinished, [], ct);
        }
    }

    [WorldOpcodeHandler(WorldOpcode.MsgRaidTargetUpdate)]
    public async Task OnRaidTargetUpdate(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        // Формат CMaNGOS: uint8 op (0 — list, 1 — set). При set: uint8 idx (0..7), packed/full guid.
        if (reader.Remaining == 0)
            return;
        var op = reader.UInt8();

        var charGuid = (ulong)session.InWorldGuid;
        var group = registry.GetByChar(charGuid);
        if (group is null)
            return;

        if (op == 0)
        {
            // Запрос текущего списка: отправить «list» обратно (T5.1 — упростим, шлём всё подряд).
            for (var i = 0; i < group.TargetIcons.Length; i++)
            {
                var pkt = new AlexWoW.Common.Network.ByteWriter(10)
                    .UInt8(1).UInt8((byte)i).UInt64(group.TargetIcons[i]).ToArray();
                await session.SendAsync(WorldOpcode.MsgRaidTargetUpdate, pkt, ct);
            }
            return;
        }

        // op == 1: set. Лидер/ассистент могут менять icons (CMaNGOS).
        var initiator = group.Members.FirstOrDefault(m => m.Guid == charGuid);
        if (initiator is null || (!group.IsLeader(charGuid) && !initiator.IsAssistant))
            return;
        if (reader.Remaining < 1)
            return;
        var idx = reader.UInt8();
        if (idx >= group.TargetIcons.Length)
            return;
        var guid = reader.UInt64();
        group.TargetIcons[idx] = guid;

        // Broadcast set всем.
        var setPkt = new AlexWoW.Common.Network.ByteWriter(10).UInt8(1).UInt8(idx).UInt64(guid).ToArray();
        foreach (var m in group.Members)
        {
            if (!m.IsOnline)
                continue;
            var p = session.World.FindPlayer(m.Guid);
            if (p is not null)
                await p.Session.SendAsync(WorldOpcode.MsgRaidTargetUpdate, setPkt, ct);
        }
    }

    // --- helpers ---

    private static Task SendPartyResultAsync(WorldSession session, GroupPackets.PartyOperation op,
        string targetName, GroupPackets.PartyResult result, CancellationToken ct)
        => session.SendAsync(WorldOpcode.SmsgPartyCommandResult,
            GroupPackets.BuildPartyCommandResult(op, targetName, result), ct);

    /// <summary>Поиск онлайн-игрока по имени (case-insensitive). Возвращает null, если не онлайн.</summary>
    private static WorldPlayer? FindOnlinePlayerByName(WorldSession session, string name)
    {
        foreach (var p in session.World.Players)
        {
            if (string.Equals(p.Character.Name, name, System.StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }
}
