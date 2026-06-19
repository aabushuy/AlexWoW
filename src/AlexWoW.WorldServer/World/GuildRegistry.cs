// Порт CMaNGOS-WoTLK: src/game/Guilds/GuildMgr.cpp (sGuildMgr lookup)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Guilds/GuildMgr.cpp. GPL-2.0.

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Реестр гильдий в мире: id → Guild + lookup charGuid → Guild + по имени.
/// Эталон — sGuildMgr (GuildMgr.cpp).
/// </summary>
internal sealed class GuildRegistry
{
    private readonly Dictionary<uint, Guild> _byId = [];
    private readonly Dictionary<ulong, Guild> _byChar = [];      // включая invites
    private readonly Dictionary<string, Guild> _byName = new(System.StringComparer.OrdinalIgnoreCase);
    private uint _nextId = 1;

    /// <summary>Создать новую гильдию с лидером (5 дефолтных рангов).</summary>
    public Guild Create(string name, ulong leaderGuid, string leaderName, byte leaderClass, byte leaderLevel)
    {
        var g = new Guild { Id = _nextId++, Name = name, LeaderGuid = leaderGuid };
        g.InitDefaultRanks();
        g.AddMember(new GuildMember
        {
            Guid = leaderGuid,
            Name = leaderName,
            Class = leaderClass,
            Level = leaderLevel,
            RankId = (byte)GuildDefaultRank.GuildMaster,
            JoinedAt = DateTime.UtcNow,
            IsOnline = true,
        });
        _byId[g.Id] = g;
        _byChar[leaderGuid] = g;
        _byName[name] = g;
        return g;
    }

    /// <summary>T5: восстановление из БД.</summary>
    public Guild Rehydrate(uint persistedId, string name, ulong leaderGuid)
    {
        var g = new Guild
        {
            Id = _nextId++,
            PersistedId = persistedId,
            Name = name,
            LeaderGuid = leaderGuid,
        };
        _byId[g.Id] = g;
        _byName[name] = g;
        return g;
    }

    public Guild? GetByChar(ulong charGuid) => _byChar.GetValueOrDefault(charGuid);
    public Guild? GetById(uint guildId) => _byId.GetValueOrDefault(guildId);
    public Guild? GetByName(string name) => _byName.GetValueOrDefault(name);
    public IEnumerable<Guild> All => _byId.Values;

    public void TrackInvite(Guild guild, ulong recipient) => _byChar[recipient] = guild;
    public void OnMemberJoined(Guild guild, ulong charGuid) => _byChar[charGuid] = guild;
    public void DetachChar(ulong charGuid) => _byChar.Remove(charGuid);

    public void Remove(Guild guild)
    {
        foreach (var m in guild.Members)
            _byChar.Remove(m.Guid);
        foreach (var iv in guild.Invites)
            _byChar.Remove(iv);
        _byChar.Remove(guild.LeaderGuid);
        _byName.Remove(guild.Name);
        _byId.Remove(guild.Id);
    }
}
