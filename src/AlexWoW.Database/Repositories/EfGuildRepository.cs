using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlexWoW.Database.Repositories;

/// <summary>EF-репозиторий персистенции гильдии (GUILD.T5).</summary>
public sealed class EfGuildRepository(IDbContextFactory<AuthDbContext> factory) : IGuildRepository
{
    public async Task<IReadOnlyList<(GuildData Guild, IReadOnlyList<GuildRank> Ranks, IReadOnlyList<GuildMemberData> Members)>>
        LoadAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var guilds = await db.Guilds.AsNoTracking().ToListAsync(ct);
        if (guilds.Count == 0)
            return [];
        var ids = guilds.ConvertAll(g => g.Id);
        var ranks = await db.GuildRanks.AsNoTracking().Where(r => ids.Contains(r.GuildId)).ToListAsync(ct);
        var members = await db.GuildMembers.AsNoTracking().Where(m => ids.Contains(m.GuildId)).ToListAsync(ct);
        return [..guilds.Select(g => (g,
            (IReadOnlyList<GuildRank>)[..ranks.Where(r => r.GuildId == g.Id).OrderBy(r => r.RankId)],
            (IReadOnlyList<GuildMemberData>)[..members.Where(m => m.GuildId == g.Id)]))];
    }

    public async Task<uint> InsertGuildAsync(GuildData guild, IReadOnlyList<GuildRank> ranks,
        GuildMemberData leader, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Guilds.Add(guild);
        await db.SaveChangesAsync(ct);
        foreach (var r in ranks)
        {
            r.GuildId = guild.Id;
            db.GuildRanks.Add(r);
        }
        leader.GuildId = guild.Id;
        db.GuildMembers.Add(leader);
        await db.SaveChangesAsync(ct);
        return guild.Id;
    }

    public async Task UpdateGuildAsync(GuildData guild, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Guilds.Update(guild);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteGuildAsync(uint guildId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.GuildMembers.Where(m => m.GuildId == guildId).ExecuteDeleteAsync(ct);
        await db.GuildRanks.Where(r => r.GuildId == guildId).ExecuteDeleteAsync(ct);
        await db.Guilds.Where(g => g.Id == guildId).ExecuteDeleteAsync(ct);
    }

    public async Task InsertMemberAsync(GuildMemberData member, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.GuildMembers.Add(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateMemberAsync(GuildMemberData member, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.GuildMembers.Update(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteMemberAsync(uint guildId, uint charGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.GuildMembers.Where(m => m.GuildId == guildId && m.CharGuid == charGuid).ExecuteDeleteAsync(ct);
    }
}
