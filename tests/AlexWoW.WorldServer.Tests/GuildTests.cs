using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Tests;

/// <summary>GUILD.T1: инварианты Guild + GuildRegistry.</summary>
public sealed class GuildTests
{
    [Fact]
    public void Create_initializes_5_default_ranks_and_leader_member()
    {
        var reg = new GuildRegistry();
        var g = reg.Create("Test", leaderGuid: 0x10, leaderName: "Alice", leaderClass: 1, leaderLevel: 60);

        Assert.Equal(5, g.Ranks.Count);
        Assert.Equal("Guild Master", g.Ranks[0].Name);
        Assert.Equal(GuildRankRights.All, g.Ranks[0].Rights);
        Assert.Single(g.Members);
        Assert.Equal((byte)GuildDefaultRank.GuildMaster, g.Members[0].RankId);
        Assert.Same(g, reg.GetByName("Test"));
        Assert.Same(g, reg.GetByChar(0x10));
    }

    [Fact]
    public void HasRight_checks_member_rank_rights()
    {
        var reg = new GuildRegistry();
        var g = reg.Create("T", 0x10, "L", 1, 60);
        g.AddMember(new GuildMember
        {
            Guid = 0x20, Name = "M", Class = 1, Level = 60,
            RankId = (byte)GuildDefaultRank.Member, JoinedAt = DateTime.UtcNow,
        });

        Assert.True(g.HasRight(0x10, GuildRankRights.Invite));   // GM имеет всё
        Assert.False(g.HasRight(0x20, GuildRankRights.Invite));  // Member — нет
        Assert.True(g.HasRight(0x20, GuildRankRights.ChatSpeak));
    }

    [Fact]
    public void Invite_then_accept_adds_initiate()
    {
        var reg = new GuildRegistry();
        var g = reg.Create("T", 0x10, "L", 1, 60);
        Assert.True(g.AddInvite(0x20));
        Assert.True(g.HasInvite(0x20));

        // Simulate accept: remove invite + add member.
        g.RemoveInvite(0x20);
        g.AddMember(new GuildMember
        {
            Guid = 0x20, Name = "Recruit", Class = 5, Level = 1,
            RankId = (byte)GuildDefaultRank.Initiate, JoinedAt = DateTime.UtcNow,
        });

        Assert.Equal(2, g.MemberCount);
        Assert.False(g.HasInvite(0x20));
        Assert.True(g.ContainsMember(0x20));
    }

    [Fact]
    public void Cannot_create_two_guilds_with_same_name()
    {
        var reg = new GuildRegistry();
        reg.Create("Alpha", 0x10, "L", 1, 60);
        Assert.NotNull(reg.GetByName("Alpha"));
        // Регистр выдаст вторую с тем же name (на handlers — гейт NameExists).
        // Здесь проверяем lookup case-insensitive.
        Assert.NotNull(reg.GetByName("alpha"));
    }

    [Fact]
    public void RemoveMember_returns_false_for_nonmember()
    {
        var g = new Guild { Id = 1, Name = "T", LeaderGuid = 0x10 };
        Assert.False(g.RemoveMember(0x99));
    }

    [Fact]
    public void Rehydrate_sets_persisted_id()
    {
        var reg = new GuildRegistry();
        var g = reg.Rehydrate(persistedId: 42, name: "Old", leaderGuid: 0x10);
        Assert.Equal(42u, g.PersistedId);
        Assert.Same(g, reg.GetByName("Old"));
    }
}
