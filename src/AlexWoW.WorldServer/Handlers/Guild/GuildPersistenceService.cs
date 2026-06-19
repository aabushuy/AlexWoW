// Порт CMaNGOS-WoTLK: src/game/Guilds/Guild.cpp (SaveGuildToDB / SaveMemberToDB)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Guilds/Guild.cpp. GPL-2.0.

using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;
using EfGuildData = AlexWoW.Database.Entities.GuildData;
using EfGuildMember = AlexWoW.Database.Entities.GuildMemberData;
using EfGuildRank = AlexWoW.Database.Entities.GuildRank;

namespace AlexWoW.WorldServer.Handlers.Guild;

/// <summary>Persistence гильдии (GUILD.T5).</summary>
internal sealed class GuildPersistenceService(IGuildRepository repo, ILogger<GuildPersistenceService> logger)
{
    public async Task SaveNewGuildAsync(World.Guild guild, World.GuildMember leader, CancellationToken ct)
    {
        try
        {
            var data = ToEf(guild);
            var ranks = guild.Ranks.Select(r => new EfGuildRank
            {
                RankId = r.Id,
                Name = r.Name,
                Rights = (uint)r.Rights,
                BankMoneyPerDay = r.BankMoneyPerDay,
            }).ToList();
            var leaderEf = new EfGuildMember
            {
                CharGuid = (uint)leader.Guid,
                RankId = leader.RankId,
                PublicNote = leader.PublicNote,
                OfficerNote = leader.OfficerNote,
                JoinedAt = leader.JoinedAt,
            };
            var newId = await repo.InsertGuildAsync(data, ranks, leaderEf, ct);
            guild.PersistedId = newId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GUILD persistence: SaveNewGuild '{Name}' failed: {Msg}", guild.Name, ex.Message);
        }
    }

    public async Task UpdateGuildAsync(World.Guild guild, CancellationToken ct)
    {
        if (guild.PersistedId == 0)
            return;
        try { await repo.UpdateGuildAsync(ToEf(guild), ct); }
        catch (Exception ex) { logger.LogWarning(ex, "GUILD persistence: UpdateGuild id={Id} failed", guild.PersistedId); }
    }

    public async Task DeleteGuildAsync(World.Guild guild, CancellationToken ct)
    {
        if (guild.PersistedId == 0)
            return;
        try { await repo.DeleteGuildAsync(guild.PersistedId, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "GUILD persistence: DeleteGuild id={Id} failed", guild.PersistedId); }
    }

    public async Task SaveMemberAsync(World.Guild guild, World.GuildMember member, CancellationToken ct)
    {
        if (guild.PersistedId == 0)
            return;
        try
        {
            await repo.InsertMemberAsync(new EfGuildMember
            {
                GuildId = guild.PersistedId,
                CharGuid = (uint)member.Guid,
                RankId = member.RankId,
                PublicNote = member.PublicNote,
                OfficerNote = member.OfficerNote,
                JoinedAt = member.JoinedAt,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GUILD persistence: SaveMember guild={Id} char={Char} failed",
                guild.PersistedId, member.Guid);
        }
    }

    public async Task UpdateMemberAsync(World.Guild guild, World.GuildMember member, CancellationToken ct)
    {
        if (guild.PersistedId == 0)
            return;
        try
        {
            await repo.UpdateMemberAsync(new EfGuildMember
            {
                GuildId = guild.PersistedId,
                CharGuid = (uint)member.Guid,
                RankId = member.RankId,
                PublicNote = member.PublicNote,
                OfficerNote = member.OfficerNote,
                JoinedAt = member.JoinedAt,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GUILD persistence: UpdateMember guild={Id} char={Char} failed",
                guild.PersistedId, member.Guid);
        }
    }

    public async Task DeleteMemberAsync(World.Guild guild, ulong charGuid, CancellationToken ct)
    {
        if (guild.PersistedId == 0)
            return;
        try { await repo.DeleteMemberAsync(guild.PersistedId, (uint)charGuid, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "GUILD persistence: DeleteMember failed: {Msg}", ex.Message); }
    }

    private static EfGuildData ToEf(World.Guild g) => new()
    {
        Id = g.PersistedId,
        Name = g.Name,
        LeaderGuid = (uint)g.LeaderGuid,
        Motd = g.Motd,
        InfoText = g.InfoText,
        CreatedAt = g.CreatedAt,
        EmblemStyle = g.EmblemStyle,
        EmblemColor = g.EmblemColor,
        BorderStyle = g.BorderStyle,
        BorderColor = g.BorderColor,
        BackgroundColor = g.BackgroundColor,
    };
}
