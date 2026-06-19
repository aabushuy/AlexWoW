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
internal sealed class GroupHandlers(GroupRegistry registry) : IOpcodeHandlerModule
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

        // SMSG_GROUP_LIST broadcast — в T2. Пока хватит того, что состояние Group в реестре.
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
        session.Logger.LogInformation("GROUP '{User}' выкинул {Guid} из группы {Group}",
            session.Account, targetGuid, group.Id);
        // SMSG_GROUP_LIST broadcast — T2.
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
