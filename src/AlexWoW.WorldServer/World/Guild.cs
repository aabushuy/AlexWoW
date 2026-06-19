// Порт CMaNGOS-WoTLK: src/game/Guilds/Guild.cpp + Guild.h
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Guilds/Guild.cpp. GPL-2.0.

namespace AlexWoW.WorldServer.World;

/// <summary>5 дефолтных рангов CMaNGOS (GR_GUILDMASTER..GR_INITIATE) — id 0..4.</summary>
internal enum GuildDefaultRank : byte
{
    GuildMaster = 0,
    Officer = 1,
    Veteran = 2,
    Member = 3,
    Initiate = 4,
}

/// <summary>Битмаск разрешений ранга (CMaNGOS GuildRankRights).</summary>
[System.Flags]
internal enum GuildRankRights : uint
{
    Empty = 0x00000040,
    ChatListen = 0x00000041,
    ChatSpeak = 0x00000042,
    OfficerChatListen = 0x00000044,
    OfficerChatSpeak = 0x00000048,
    Promote = 0x000000C0,
    Demote = 0x00000140,
    Invite = 0x00000050,
    Remove = 0x00000060,
    SetMotd = 0x00001040,
    EditPublicNote = 0x00002040,
    ViewOfficerNote = 0x00004040,
    EditOfficerNote = 0x00008040,
    ModifyGuildInfo = 0x00010040,
    WithdrawGoldLock = 0x00020000,
    WithdrawRepair = 0x00040000,
    WithdrawGold = 0x00080000,
    CreateGuildEvent = 0x00100000,
    All = 0x001DF1FF,
}

/// <summary>Коды ошибок SMSG_GUILD_COMMAND_RESULT (CMaNGOS CommandErrors).</summary>
internal enum GuildCommandError : uint
{
    Ok = 0x00,
    Internal = 0x01,
    AlreadyInGuild = 0x02,
    AlreadyInGuildS = 0x03,
    InvitedToGuild = 0x04,
    AlreadyInvitedS = 0x05,
    NameInvalid = 0x06,
    NameExistsS = 0x07,
    Permissions = 0x08,
    NotInGuild = 0x09,
    NotInGuildS = 0x0A,
    PlayerNotFoundS = 0x0B,
    NotAllied = 0x0C,
    RankTooHighS = 0x0D,
    RankTooLowS = 0x0E,
    IgnoringYouS = 0x13,
}

/// <summary>Тип команды для SMSG_GUILD_COMMAND_RESULT (CMaNGOS Typecommand).</summary>
internal enum GuildCommandType : uint
{
    Create = 0x00,
    Invite = 0x01,
    Quit = 0x03,
    Founder = 0x0E,
}

/// <summary>События для SMSG_GUILD_EVENT (CMaNGOS GuildEvents).</summary>
internal enum GuildEvent : byte
{
    Promotion = 0x00,
    Demotion = 0x01,
    Motd = 0x02,
    Joined = 0x03,
    Left = 0x04,
    Removed = 0x05,
    LeaderIs = 0x06,
    LeaderChanged = 0x07,
    Disbanded = 0x08,
    TabardChange = 0x09,
    SignedOn = 0x0C,
    SignedOff = 0x0D,
}

/// <summary>Ранг гильдии: id + имя + права + лимит вывода золота из банка.</summary>
internal sealed class GuildRank
{
    public byte Id { get; init; }
    public required string Name { get; set; }
    public GuildRankRights Rights { get; set; }
    public int BankMoneyPerDay { get; set; } = -1; // -1 — без лимита (T4)
}

/// <summary>Член гильдии: GUID + ранг + публичная/офицерская заметки.</summary>
internal sealed class GuildMember
{
    public ulong Guid { get; init; }
    public required string Name { get; init; }
    public byte Class { get; init; }
    public byte Level { get; set; }
    public byte RankId { get; set; }
    public string PublicNote { get; set; } = "";
    public string OfficerNote { get; set; } = "";
    public bool IsOnline { get; set; } = true;
    public ushort Zone { get; set; }
    public DateTime JoinedAt { get; init; }
    public DateTime? LastLogoutAt { get; set; }
}

/// <summary>
/// Гильдия. Эталон — CMaNGOS class Guild (src/game/Guilds/Guild.h).
/// </summary>
/// <remarks>
/// T1 покрывает: create/invite/accept/decline/leave/disband. Roster sync — T2. Promote/demote — T3.
/// MOTD/info_text/notes — T4. Persistence — T5.
/// </remarks>
internal sealed class Guild
{
    /// <summary>In-memory id (для GroupRegistry-like lookup). PersistedId — отдельный (T5).</summary>
    public uint Id { get; init; }
    public uint PersistedId { get; set; }

    public required string Name { get; init; }
    public ulong LeaderGuid { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public string Motd { get; set; } = "";
    public string InfoText { get; set; } = "";

    /// <summary>Эмблема (T4): style/color/border/border_color/background_color.</summary>
    public uint EmblemStyle { get; set; }
    public uint EmblemColor { get; set; }
    public uint BorderStyle { get; set; }
    public uint BorderColor { get; set; }
    public uint BackgroundColor { get; set; }

    private readonly List<GuildRank> _ranks = [];
    private readonly List<GuildMember> _members = [];
    private readonly HashSet<ulong> _invites = [];

    public IReadOnlyList<GuildRank> Ranks => _ranks;
    public IReadOnlyList<GuildMember> Members => _members;
    public IReadOnlyCollection<ulong> Invites => _invites;
    public int MemberCount => _members.Count;

    public bool IsLeader(ulong guid) => LeaderGuid == guid;
    public GuildMember? FindMember(ulong guid) => _members.Find(m => m.Guid == guid);
    public bool ContainsMember(ulong guid) => _members.Exists(m => m.Guid == guid);

    /// <summary>Заполнить 5 дефолтных рангов (CMaNGOS Guild::CreateDefaultGuildRanks).</summary>
    public void InitDefaultRanks()
    {
        _ranks.Clear();
        _ranks.Add(new GuildRank { Id = 0, Name = "Guild Master", Rights = GuildRankRights.All });
        _ranks.Add(new GuildRank
        {
            Id = 1,
            Name = "Officer",
            Rights = GuildRankRights.ChatListen | GuildRankRights.ChatSpeak
                   | GuildRankRights.OfficerChatListen | GuildRankRights.OfficerChatSpeak
                   | GuildRankRights.Promote | GuildRankRights.Demote
                   | GuildRankRights.Invite | GuildRankRights.Remove
                   | GuildRankRights.SetMotd | GuildRankRights.EditPublicNote
                   | GuildRankRights.ViewOfficerNote | GuildRankRights.EditOfficerNote,
        });
        _ranks.Add(new GuildRank
        {
            Id = 2,
            Name = "Veteran",
            Rights = GuildRankRights.ChatListen | GuildRankRights.ChatSpeak
                   | GuildRankRights.Invite | GuildRankRights.ViewOfficerNote,
        });
        _ranks.Add(new GuildRank
        {
            Id = 3,
            Name = "Member",
            Rights = GuildRankRights.ChatListen | GuildRankRights.ChatSpeak,
        });
        _ranks.Add(new GuildRank
        {
            Id = 4,
            Name = "Initiate",
            Rights = GuildRankRights.ChatListen,
        });
    }

    public bool AddInvite(ulong guid)
    {
        if (_invites.Contains(guid) || ContainsMember(guid))
            return false;
        _invites.Add(guid);
        return true;
    }
    public bool HasInvite(ulong guid) => _invites.Contains(guid);
    public void RemoveInvite(ulong guid) => _invites.Remove(guid);

    public bool AddMember(GuildMember member)
    {
        if (ContainsMember(member.Guid))
            return false;
        _members.Add(member);
        _invites.Remove(member.Guid);
        return true;
    }

    public bool RemoveMember(ulong guid)
    {
        var idx = _members.FindIndex(m => m.Guid == guid);
        if (idx < 0)
            return false;
        _members.RemoveAt(idx);
        return true;
    }

    /// <summary>
    /// Проверка наличия конкретного права у члена. CMaNGOS флаги имеют общий бит 6 (Empty=0x40),
    /// поэтому проверяем строгое совпадение всех битов, а не overlap.
    /// </summary>
    public bool HasRight(ulong charGuid, GuildRankRights right)
    {
        var m = FindMember(charGuid);
        if (m is null)
            return false;
        var rank = _ranks.Find(r => r.Id == m.RankId);
        return rank is not null && (rank.Rights & right) == right;
    }
}
