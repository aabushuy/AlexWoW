// Порт CMaNGOS-WoTLK: src/game/Guilds/GuildHandler.cpp (Roster/Promote/Demote/Remove/MOTD/Notes)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Guilds/GuildHandler.cpp. GPL-2.0.

using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Guild;

/// <summary>
/// Опкод-модуль гильдии (GUILD.T2/T3/T4): roster sync, promote/demote/remove/leader/ranks, MOTD/notes.
/// </summary>
internal sealed class GuildRosterHandlers(GuildRegistry registry, GuildPersistenceService persist) : IOpcodeHandlerModule
{
    [WorldOpcodeHandler(WorldOpcode.CmsgGuildRoster)]
    public async Task OnGuildRoster(WorldSession session, IncomingPacket _, CancellationToken ct)
    {
        var charGuid = (ulong)session.InWorldGuid;
        var guild = registry.GetByChar(charGuid);
        if (guild is null)
            return;
        var canView = guild.HasRight(charGuid, GuildRankRights.ViewOfficerNote);
        await session.SendAsync(WorldOpcode.SmsgGuildRoster,
            GuildPackets.BuildGuildRoster(guild, canView), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildPromote)]
    public async Task OnGuildPromote(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => await ChangeRankAsync(session, packet, GuildRankRights.Promote, GuildEvent.Promotion, delta: -1, ct);

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildDemote)]
    public async Task OnGuildDemote(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => await ChangeRankAsync(session, packet, GuildRankRights.Demote, GuildEvent.Demotion, delta: 1, ct);

    /// <summary>
    /// CMaNGOS: PROMOTE → rank--, DEMOTE → rank++. Нельзя promote выше GM, нельзя ниже последнего ранга.
    /// Инициатор должен иметь strictly higher rank, чем цель.
    /// </summary>
    private async Task ChangeRankAsync(WorldSession session, IncomingPacket packet, GuildRankRights right,
        GuildEvent ev, int delta, CancellationToken ct)
    {
        var targetName = packet.Reader().CString();
        var charGuid = (ulong)session.InWorldGuid;
        var guild = registry.GetByChar(charGuid);
        if (guild is null)
            return;
        var initiator = guild.FindMember(charGuid);
        if (initiator is null || !guild.HasRight(charGuid, right))
        {
            await SendResult(session, GuildCommandType.Invite, "", GuildCommandError.Permissions, ct);
            return;
        }
        var target = guild.Members.FirstOrDefault(m =>
            string.Equals(m.Name, targetName, System.StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            await SendResult(session, GuildCommandType.Invite, targetName, GuildCommandError.PlayerNotFoundS, ct);
            return;
        }
        // Strictly higher rank: меньший id = выше. Initiator.RankId < target.RankId.
        if (initiator.RankId >= target.RankId)
        {
            await SendResult(session, GuildCommandType.Invite, targetName, GuildCommandError.RankTooHighS, ct);
            return;
        }
        var newRank = target.RankId + delta;
        if (newRank <= initiator.RankId)
        {
            await SendResult(session, GuildCommandType.Invite, targetName, GuildCommandError.RankTooHighS, ct);
            return;
        }
        if (newRank < 0 || newRank >= guild.Ranks.Count)
        {
            await SendResult(session, GuildCommandType.Invite, targetName,
                delta < 0 ? GuildCommandError.RankTooHighS : GuildCommandError.RankTooLowS, ct);
            return;
        }

        target.RankId = (byte)newRank;
        await persist.UpdateMemberAsync(guild, target, ct); // T5
        await GuildHandlers.BroadcastEvent(guild, ev, [session.Character?.Name ?? "", target.Name,
            guild.Ranks[newRank].Name], session.World, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildRemove)]
    public async Task OnGuildRemove(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var targetName = packet.Reader().CString();
        var charGuid = (ulong)session.InWorldGuid;
        var guild = registry.GetByChar(charGuid);
        if (guild is null)
            return;
        var initiator = guild.FindMember(charGuid);
        if (initiator is null || !guild.HasRight(charGuid, GuildRankRights.Remove))
        {
            await SendResult(session, GuildCommandType.Invite, "", GuildCommandError.Permissions, ct);
            return;
        }
        var target = guild.Members.FirstOrDefault(m =>
            string.Equals(m.Name, targetName, System.StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            await SendResult(session, GuildCommandType.Invite, targetName, GuildCommandError.PlayerNotFoundS, ct);
            return;
        }
        // Нельзя кикнуть GM или кого-то одного ранга/выше.
        if (target.Guid == guild.LeaderGuid || initiator.RankId >= target.RankId)
        {
            await SendResult(session, GuildCommandType.Invite, targetName, GuildCommandError.RankTooHighS, ct);
            return;
        }

        guild.RemoveMember(target.Guid);
        registry.DetachChar(target.Guid);
        await persist.DeleteMemberAsync(guild, target.Guid, ct); // T5
        await GuildHandlers.BroadcastEvent(guild, GuildEvent.Removed,
            [target.Name, session.Character?.Name ?? ""], session.World, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildLeader)]
    public async Task OnGuildLeader(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var targetName = packet.Reader().CString();
        var charGuid = (ulong)session.InWorldGuid;
        var guild = registry.GetByChar(charGuid);
        if (guild is null || !guild.IsLeader(charGuid))
        {
            await SendResult(session, GuildCommandType.Invite, "", GuildCommandError.Permissions, ct);
            return;
        }
        var target = guild.Members.FirstOrDefault(m =>
            string.Equals(m.Name, targetName, System.StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            await SendResult(session, GuildCommandType.Invite, targetName, GuildCommandError.PlayerNotFoundS, ct);
            return;
        }
        var oldLeader = guild.FindMember(charGuid);
        if (oldLeader is null)
            return;

        // Меняемся ранками: старый GM → Officer, новый → GM.
        target.RankId = (byte)GuildDefaultRank.GuildMaster;
        oldLeader.RankId = (byte)GuildDefaultRank.Officer;
        guild.LeaderGuid = target.Guid;

        await persist.UpdateGuildAsync(guild, ct); // T5
        await persist.UpdateMemberAsync(guild, target, ct);
        await persist.UpdateMemberAsync(guild, oldLeader, ct);
        await GuildHandlers.BroadcastEvent(guild, GuildEvent.LeaderChanged,
            [oldLeader.Name, target.Name], session.World, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildMotd)]
    public async Task OnGuildMotd(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var motd = packet.Reader().CString();
        if (motd.Length > 128) motd = motd[..128];
        var charGuid = (ulong)session.InWorldGuid;
        var guild = registry.GetByChar(charGuid);
        if (guild is null || !guild.HasRight(charGuid, GuildRankRights.SetMotd))
        {
            await SendResult(session, GuildCommandType.Invite, "", GuildCommandError.Permissions, ct);
            return;
        }
        guild.Motd = motd;
        await persist.UpdateGuildAsync(guild, ct); // T5
        await GuildHandlers.BroadcastEvent(guild, GuildEvent.Motd, [motd], session.World, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildSetPublicNote)]
    public Task OnGuildSetPublicNote(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => SetNoteAsync(session, packet, officer: false, ct);

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildSetOfficerNote)]
    public Task OnGuildSetOfficerNote(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => SetNoteAsync(session, packet, officer: true, ct);

    private Task SetNoteAsync(WorldSession session, IncomingPacket packet, bool officer, CancellationToken ct)
    {
        var reader = packet.Reader();
        var name = reader.CString();
        var note = reader.CString();
        if (note.Length > 31) note = note[..31];
        var charGuid = (ulong)session.InWorldGuid;
        var guild = registry.GetByChar(charGuid);
        if (guild is null)
            return Task.CompletedTask;

        var right = officer ? GuildRankRights.EditOfficerNote : GuildRankRights.EditPublicNote;
        if (!guild.HasRight(charGuid, right))
            return SendResult(session, GuildCommandType.Invite, "", GuildCommandError.Permissions, ct);

        var target = guild.Members.FirstOrDefault(m =>
            string.Equals(m.Name, name, System.StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return Task.CompletedTask;

        if (officer)
            target.OfficerNote = note;
        else
            target.PublicNote = note;
        return persist.UpdateMemberAsync(guild, target, ct); // T5
    }

    // --- helpers ---

    private static Task SendResult(WorldSession session, GuildCommandType cmd, string name,
        GuildCommandError err, CancellationToken ct)
        => session.SendAsync(WorldOpcode.SmsgGuildCommandResult,
            GuildPackets.BuildCommandResult(cmd, name, err), ct);
}
