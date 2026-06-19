using AlexWoW.Database.Entities;

namespace AlexWoW.Database.Abstractions;

/// <summary>Persistence гильдии (GUILD.T5).</summary>
public interface IGuildRepository
{
    Task<IReadOnlyList<(GuildData Guild, IReadOnlyList<GuildRank> Ranks, IReadOnlyList<GuildMemberData> Members)>>
        LoadAllAsync(CancellationToken ct = default);

    Task<uint> InsertGuildAsync(GuildData guild, IReadOnlyList<GuildRank> ranks, GuildMemberData leader, CancellationToken ct = default);
    Task UpdateGuildAsync(GuildData guild, CancellationToken ct = default);
    Task DeleteGuildAsync(uint guildId, CancellationToken ct = default);

    Task InsertMemberAsync(GuildMemberData member, CancellationToken ct = default);
    Task UpdateMemberAsync(GuildMemberData member, CancellationToken ct = default);
    Task DeleteMemberAsync(uint guildId, uint charGuid, CancellationToken ct = default);
}
