namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>guild_data</c> (БД alexwow_auth). GUILD.T5.</summary>
public sealed class GuildData
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public uint LeaderGuid { get; set; }
    public string Motd { get; set; } = "";
    public string InfoText { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public uint EmblemStyle { get; set; }
    public uint EmblemColor { get; set; }
    public uint BorderStyle { get; set; }
    public uint BorderColor { get; set; }
    public uint BackgroundColor { get; set; }
}

/// <summary>EF-сущность таблицы <c>guild_rank</c>. Composite PK (GuildId, RankId).</summary>
public sealed class GuildRank
{
    public uint GuildId { get; set; }
    public byte RankId { get; set; }
    public string Name { get; set; } = "";
    public uint Rights { get; set; }
    public int BankMoneyPerDay { get; set; } = -1;
}

/// <summary>EF-сущность таблицы <c>guild_member</c>. Composite PK (GuildId, CharGuid).</summary>
public sealed class GuildMemberData
{
    public uint GuildId { get; set; }
    public uint CharGuid { get; set; }
    public byte RankId { get; set; }
    public string PublicNote { get; set; } = "";
    public string OfficerNote { get; set; } = "";
    public DateTime JoinedAt { get; set; }
}
