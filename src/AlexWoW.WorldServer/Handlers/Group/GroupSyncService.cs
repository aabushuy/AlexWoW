// Порт CMaNGOS-WoTLK: src/game/Groups/Group.cpp (SendUpdate/SendUpdateTo/UpdatePlayerOnlineStatus)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Groups/Group.cpp. GPL-2.0.

using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Group;

/// <summary>
/// Синхронизация состава группы и статусов членов с клиентами (GROUP.T2):
/// SMSG_GROUP_LIST на изменениях состава + SMSG_PARTY_MEMBER_STATS на изменениях статуса.
/// </summary>
/// <remarks>
/// SendGroupListAsync вызывается из GroupHandlers после AddMember/RemoveMember.
/// MarkOnlineAsync/MarkOfflineAsync — на login/logout (T2 через LoginSequenceService).
/// Периодическая рассылка stats в T2.1 (CMaNGOS Group::UpdatePlayerOutOfRange) — пока шлём
/// один раз вместе с GROUP_LIST, чего достаточно для отображения панели партии.
/// </remarks>
internal sealed class GroupSyncService(GroupRegistry registry, ILogger<GroupSyncService> logger)
{
    /// <summary>SMSG_GROUP_LIST всем онлайн-членам группы; за каждого считаем свою «receiver-шапку».</summary>
    public async Task SendGroupListAsync(World.Group group, WorldState world, CancellationToken ct)
    {
        foreach (var m in group.Members.ToList())
        {
            if (!m.IsOnline)
                continue;
            var player = world.FindPlayer(m.Guid);
            if (player is null)
            {
                m.IsOnline = false; // защита от рассинхрона
                continue;
            }
            var bytes = GroupPackets.BuildGroupList(group, m);
            await player.Session.SendAsync(WorldOpcode.SmsgGroupList, bytes, ct);
        }
    }

    /// <summary>SMSG_PARTY_MEMBER_STATS по каждому онлайн-члену группы — full snapshot (T2).</summary>
    public async Task SendAllStatsAsync(World.Group group, WorldState world, CancellationToken ct)
    {
        foreach (var m in group.Members.ToList())
        {
            if (!m.IsOnline)
                continue;
            var player = world.FindPlayer(m.Guid);
            if (player is null)
                continue;
            var bytes = BuildFullStats(player);
            // Рассылаем стату каждого члена всем (включая ему самому).
            foreach (var recv in group.Members)
            {
                if (!recv.IsOnline)
                    continue;
                var rcv = world.FindPlayer(recv.Guid);
                if (rcv is null)
                    continue;
                await rcv.Session.SendAsync(WorldOpcode.SmsgPartyMemberStats, bytes, ct);
            }
        }
    }

    /// <summary>
    /// Игрок вошёл в мир: если он в группе — пометить online и пересинхронизировать всю группу.
    /// </summary>
    public async Task MarkOnlineAsync(WorldPlayer player, CancellationToken ct)
    {
        var group = registry.GetByChar(player.Guid);
        if (group is null)
            return;
        var member = group.Members.FirstOrDefault(m => m.Guid == player.Guid);
        if (member is null)
            return;
        member.IsOnline = true;
        await SendGroupListAsync(group, player.Session.World, ct);
        await SendAllStatsAsync(group, player.Session.World, ct);
        logger.LogInformation("GROUP member {Guid} ONLINE (group {GroupId}, members {N})",
            player.Guid, group.Id, group.MemberCount);
    }

    /// <summary>
    /// Игрок ушёл из мира: пометить offline; разослать остальным MEMBER_STATS со Status=Offline.
    /// </summary>
    public async Task MarkOfflineAsync(WorldPlayer player, CancellationToken ct)
    {
        var group = registry.GetByChar(player.Guid);
        if (group is null)
            return;
        var member = group.Members.FirstOrDefault(m => m.Guid == player.Guid);
        if (member is null)
            return;
        member.IsOnline = false;

        // T3: если ушёл лидер — promote первого онлайн-наследника. Если он один остался — оффлайн-лидер
        // остаётся, всё распадётся по таймеру в T6. Пока: безусловный promote если есть онлайн-наследник.
        if (group.IsLeader(player.Guid))
        {
            var heir = group.PickOnlineHeirExceptLeader();
            if (heir is not null && group.ChangeLeader(heir.Guid) is { } newName)
            {
                var pkt = GroupPackets.BuildGroupSetLeader(newName);
                foreach (var m in group.Members)
                {
                    if (!m.IsOnline)
                        continue;
                    var p = player.Session.World.FindPlayer(m.Guid);
                    if (p is not null)
                        await p.Session.SendAsync(WorldOpcode.SmsgGroupSetLeader, pkt, ct);
                }
                await SendGroupListAsync(group, player.Session.World, ct);
                logger.LogInformation("GROUP leader {OldGuid} offline → promote {NewGuid}", player.Guid, heir.Guid);
            }
        }

        // Off-line stats: только status=Offline + HP/MP=0.
        var bytes = GroupPackets.BuildPartyMemberStats(player.Guid,
            GroupPackets.GroupUpdateFlag.Status | GroupPackets.GroupUpdateFlag.CurHp,
            GroupPackets.MemberStatus.Offline, curHp: 0, maxHp: 0, powerType: 0,
            curPower: 0, maxPower: 0, level: 0, zone: 0, posX: 0, posY: 0);

        foreach (var recv in group.Members.ToList())
        {
            if (!recv.IsOnline || recv.Guid == player.Guid)
                continue;
            var rcv = player.Session.World.FindPlayer(recv.Guid);
            if (rcv is null)
                continue;
            await rcv.Session.SendAsync(WorldOpcode.SmsgPartyMemberStats, bytes, ct);
        }
        logger.LogInformation("GROUP member {Guid} OFFLINE (group {GroupId})", player.Guid, group.Id);
    }

    /// <summary>Полный snapshot статуса игрока — все базовые поля (CMaNGOS GROUP_UPDATE_FULL).</summary>
    private static byte[] BuildFullStats(WorldPlayer player)
    {
        var s = player.Session;
        var ch = s.Character!;
        // Тип энергии и значения: у каждого класса свой основной ресурс. Тут — мана если нет ярости/энергии/RP.
        byte powerType = 0; // MANA по умолчанию
        ushort curPower = 0, maxPower = 0;
        if (s.Combat.Rage > 0) { powerType = 1; curPower = (ushort)s.Combat.Rage; maxPower = 1000; }
        else if (s.Combat.Energy > 0) { powerType = 3; curPower = (ushort)s.Combat.Energy; maxPower = 100; }
        else if (s.Combat.RunicPower > 0) { powerType = 6; curPower = (ushort)s.Combat.RunicPower; maxPower = 1000; }

        return GroupPackets.BuildPartyMemberStats(player.Guid,
            GroupPackets.GroupUpdateFlag.Status
              | GroupPackets.GroupUpdateFlag.CurHp | GroupPackets.GroupUpdateFlag.MaxHp
              | GroupPackets.GroupUpdateFlag.PowerType
              | GroupPackets.GroupUpdateFlag.CurPower | GroupPackets.GroupUpdateFlag.MaxPower
              | GroupPackets.GroupUpdateFlag.Level
              | GroupPackets.GroupUpdateFlag.Zone
              | GroupPackets.GroupUpdateFlag.Position,
            GroupPackets.MemberStatus.Online,
            curHp: s.Combat.Health, maxHp: s.Combat.MaxHealth,
            powerType, curPower, maxPower,
            level: ch.Level, zone: (ushort)ch.Zone,
            posX: (ushort)player.X, posY: (ushort)player.Y);
    }
}
