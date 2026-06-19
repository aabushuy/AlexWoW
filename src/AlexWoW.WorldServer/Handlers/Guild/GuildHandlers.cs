// Порт CMaNGOS-WoTLK: src/game/Guilds/GuildHandler.cpp (HandleGuild*Opcode)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Guilds/GuildHandler.cpp. GPL-2.0.

using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Guild;

/// <summary>
/// Опкод-модуль гильдии (GUILD.T1): create/invite/accept/decline/leave/disband + query.
/// </summary>
/// <remarks>
/// T1 покрывает базовый цикл формирования. Roster (T2), promote/demote/ranks (T3),
/// MOTD/info_text/notes (T4), persistence (T5) — следующие таски эпика.
/// </remarks>
internal sealed class GuildHandlers(GuildRegistry registry, GuildPersistenceService persist) : IOpcodeHandlerModule
{
    private const int MinGuildNameLen = 4;
    private const int MaxGuildNameLen = 24;

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildCreate)]
    public async Task OnGuildCreate(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var name = packet.Reader().CString();
        var charGuid = (ulong)session.InWorldGuid;

        if (registry.GetByChar(charGuid) is not null)
        {
            await SendResult(session, GuildCommandType.Create, "", GuildCommandError.AlreadyInGuild, ct);
            return;
        }
        if (name.Length is < MinGuildNameLen or > MaxGuildNameLen)
        {
            await SendResult(session, GuildCommandType.Create, name, GuildCommandError.NameInvalid, ct);
            return;
        }
        if (registry.GetByName(name) is not null)
        {
            await SendResult(session, GuildCommandType.Create, name, GuildCommandError.NameExistsS, ct);
            return;
        }

        var ch = session.Character!;
        var g = registry.Create(name, charGuid, ch.Name, ch.Class, ch.Level);
        registry.OnMemberJoined(g, charGuid);
        await persist.SaveNewGuildAsync(g, g.Members[0], ct); // T5

        await SendResult(session, GuildCommandType.Create, name, GuildCommandError.Ok, ct);
        // Broadcast самому себе: GE_LEADER_IS, чтобы клиент сразу подхватил гильдию.
        await BroadcastEvent(g, GuildEvent.LeaderIs, [ch.Name], session.World, ct);

        session.Logger.LogInformation("GUILD '{User}' создал гильдию '{Name}' (id {Id})",
            session.Account, name, g.Id);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildInvite)]
    public async Task OnGuildInvite(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var name = packet.Reader().CString();
        var initiatorGuid = (ulong)session.InWorldGuid;
        var guild = registry.GetByChar(initiatorGuid);
        if (guild is null)
        {
            await SendResult(session, GuildCommandType.Invite, "", GuildCommandError.NotInGuild, ct);
            return;
        }
        if (!guild.HasRight(initiatorGuid, GuildRankRights.Invite))
        {
            await SendResult(session, GuildCommandType.Invite, "", GuildCommandError.Permissions, ct);
            return;
        }

        var recipient = FindOnlinePlayerByName(session, name);
        if (recipient is null)
        {
            await SendResult(session, GuildCommandType.Invite, name, GuildCommandError.PlayerNotFoundS, ct);
            return;
        }
        if (recipient.Guid == initiatorGuid)
            return;

        var recipientGuild = registry.GetByChar(recipient.Guid);
        if (recipientGuild is not null)
        {
            if (recipientGuild.ContainsMember(recipient.Guid))
                await SendResult(session, GuildCommandType.Invite, name, GuildCommandError.AlreadyInGuildS, ct);
            else
                await SendResult(session, GuildCommandType.Invite, name, GuildCommandError.AlreadyInvitedS, ct);
            return;
        }

        if (!guild.AddInvite(recipient.Guid))
            return;
        registry.TrackInvite(guild, recipient.Guid);

        await SendResult(session, GuildCommandType.Invite, name, GuildCommandError.Ok, ct);
        await recipient.Session.SendAsync(WorldOpcode.SmsgGuildInvite,
            GuildPackets.BuildGuildInvite(session.Character?.Name ?? "", guild.Name), ct);

        session.Logger.LogInformation("GUILD '{User}' приглашает '{Name}' в '{Guild}'",
            session.Account, name, guild.Name);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildAccept)]
    public async Task OnGuildAccept(WorldSession session, IncomingPacket _, CancellationToken ct)
    {
        var charGuid = (ulong)session.InWorldGuid;
        var guild = registry.GetByChar(charGuid);
        if (guild is null || !guild.HasInvite(charGuid))
            return;

        var ch = session.Character!;
        guild.RemoveInvite(charGuid);
        var newMember = new GuildMember
        {
            Guid = charGuid,
            Name = ch.Name,
            Class = ch.Class,
            Level = ch.Level,
            RankId = (byte)GuildDefaultRank.Initiate,
            JoinedAt = DateTime.UtcNow,
        };
        guild.AddMember(newMember);
        registry.OnMemberJoined(guild, charGuid);
        await persist.SaveMemberAsync(guild, newMember, ct); // T5

        await SendResult(session, GuildCommandType.Invite, guild.Name, GuildCommandError.Ok, ct);
        // GE_JOINED — broadcast всем онлайн-членам.
        await BroadcastEvent(guild, GuildEvent.Joined, [ch.Name], session.World, ct);
        session.Logger.LogInformation("GUILD '{User}' joined '{Name}'", session.Account, guild.Name);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildDecline)]
    public async Task OnGuildDecline(WorldSession session, IncomingPacket _, CancellationToken ct)
    {
        var charGuid = (ulong)session.InWorldGuid;
        var guild = registry.GetByChar(charGuid);
        if (guild is null || !guild.HasInvite(charGuid))
            return;

        guild.RemoveInvite(charGuid);
        registry.DetachChar(charGuid);

        var leader = session.World.FindPlayer(guild.LeaderGuid);
        if (leader is not null)
        {
            var myName = session.Character?.Name ?? "";
            await leader.Session.SendAsync(WorldOpcode.SmsgGuildDecline,
                GuildPackets.BuildGuildDecline(myName), ct);
        }
        session.Logger.LogInformation("GUILD '{User}' declined invite to '{Name}'", session.Account, guild.Name);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildLeave)]
    public async Task OnGuildLeave(WorldSession session, IncomingPacket _, CancellationToken ct)
    {
        var charGuid = (ulong)session.InWorldGuid;
        var guild = registry.GetByChar(charGuid);
        if (guild is null)
            return;

        // CMaNGOS: GM не может leave — должен либо disband, либо передать GM.
        if (guild.IsLeader(charGuid))
        {
            await SendResult(session, GuildCommandType.Quit, "", GuildCommandError.Permissions, ct);
            return;
        }

        var member = guild.FindMember(charGuid);
        if (member is null)
            return;
        guild.RemoveMember(charGuid);
        registry.DetachChar(charGuid);
        await persist.DeleteMemberAsync(guild, charGuid, ct); // T5

        await SendResult(session, GuildCommandType.Quit, guild.Name, GuildCommandError.Ok, ct);
        await BroadcastEvent(guild, GuildEvent.Left, [member.Name], session.World, ct);

        session.Logger.LogInformation("GUILD '{User}' left '{Name}'", session.Account, guild.Name);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildDisband)]
    public async Task OnGuildDisband(WorldSession session, IncomingPacket _, CancellationToken ct)
    {
        var charGuid = (ulong)session.InWorldGuid;
        var guild = registry.GetByChar(charGuid);
        if (guild is null || !guild.IsLeader(charGuid))
        {
            await SendResult(session, GuildCommandType.Quit, "", GuildCommandError.Permissions, ct);
            return;
        }

        // Уведомить всех онлайн-членов.
        await BroadcastEvent(guild, GuildEvent.Disbanded, [], session.World, ct);

        foreach (var m in guild.Members.ToList())
            registry.DetachChar(m.Guid);
        foreach (var iv in guild.Invites.ToList())
            registry.DetachChar(iv);
        await persist.DeleteGuildAsync(guild, ct); // T5
        registry.Remove(guild);

        session.Logger.LogInformation("GUILD '{User}' disbanded '{Name}'", session.Account, guild.Name);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildQuery)]
    public async Task OnGuildQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var guildId = reader.UInt32();
        var guild = registry.All.FirstOrDefault(g => (g.PersistedId != 0 ? g.PersistedId : g.Id) == guildId);
        if (guild is null)
            return;
        await session.SendAsync(WorldOpcode.SmsgGuildQueryResponse,
            GuildPackets.BuildGuildQueryResponse(guild), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGuildInfo)]
    public async Task OnGuildInfo(WorldSession session, IncomingPacket _, CancellationToken ct)
    {
        var charGuid = (ulong)session.InWorldGuid;
        var guild = registry.GetByChar(charGuid);
        if (guild is null)
            return;
        await session.SendAsync(WorldOpcode.SmsgGuildInfo, GuildPackets.BuildGuildInfo(guild), ct);
    }

    // --- helpers ---

    private static Task SendResult(WorldSession session, GuildCommandType cmd, string name,
        GuildCommandError err, CancellationToken ct)
        => session.SendAsync(WorldOpcode.SmsgGuildCommandResult,
            GuildPackets.BuildCommandResult(cmd, name, err), ct);

    /// <summary>Broadcast SMSG_GUILD_EVENT всем онлайн-членам.</summary>
    internal static async Task BroadcastEvent(World.Guild guild, GuildEvent ev, string[] strings,
        WorldState world, CancellationToken ct)
    {
        var bytes = GuildPackets.BuildGuildEvent(ev, strings);
        foreach (var m in guild.Members)
        {
            if (!m.IsOnline)
                continue;
            var p = world.FindPlayer(m.Guid);
            if (p is null)
                continue;
            await p.Session.SendAsync(WorldOpcode.SmsgGuildEvent, bytes, ct);
        }
    }

    /// <summary>Поиск онлайн-игрока по имени (case-insensitive).</summary>
    private static WorldPlayer? FindOnlinePlayerByName(WorldSession session, string name)
    {
        foreach (var p in session.World.Players)
            if (string.Equals(p.Character.Name, name, System.StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }
}

