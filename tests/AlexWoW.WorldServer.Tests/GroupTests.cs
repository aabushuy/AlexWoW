using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Tests;

/// <summary>GROUP.T1: invariants класса Group + GroupRegistry (без сетевой части).</summary>
public sealed class GroupTests
{
    [Fact]
    public void New_invite_only_group_is_not_created_yet()
    {
        var reg = new GroupRegistry();
        var g = reg.CreateInviteOnly(leaderGuid: 1, leaderName: "Leader");

        Assert.Equal(0, g.MemberCount);
        Assert.False(g.IsCreated);
        Assert.Equal(GroupType.Party, g.Type);
        Assert.Equal(5, g.MaxSize);
    }

    [Fact]
    public void Invite_then_accept_creates_group_with_leader_first()
    {
        var reg = new GroupRegistry();
        var g = reg.CreateInviteOnly(leaderGuid: 1, leaderName: "Leader");

        Assert.True(g.AddInvite(guid: 2));
        Assert.True(g.HasInvite(2));

        // Simulate accept flow: первый AddMember добавляет лидера, затем приглашённого.
        Assert.False(g.IsCreated);
        g.AddMember(g.LeaderGuid, g.LeaderName);
        g.AddMember(2, "Recipient");

        Assert.True(g.IsCreated);
        Assert.Equal(2, g.MemberCount);
        Assert.False(g.HasInvite(2));
        Assert.True(g.ContainsMember(2));
    }

    [Fact]
    public void Group_is_full_at_five_members_for_party()
    {
        var g = new Group { Id = 1, LeaderGuid = 1, LeaderName = "L" };
        for (ulong guid = 1; guid <= 5; guid++)
            Assert.True(g.AddMember(guid, $"P{guid}"));
        Assert.True(g.IsFull);
        Assert.False(g.AddMember(99, "Late"));
    }

    [Fact]
    public void Raid_allows_up_to_forty_members()
    {
        var g = new Group { Id = 1, LeaderGuid = 1, LeaderName = "L", Type = GroupType.Raid };
        Assert.Equal(40, g.MaxSize);
        for (ulong guid = 1; guid <= 40; guid++)
            Assert.True(g.AddMember(guid, $"P{guid}"));
        Assert.True(g.IsFull);
    }

    [Fact]
    public void Duplicate_invite_or_member_is_rejected()
    {
        var g = new Group { Id = 1, LeaderGuid = 1, LeaderName = "L" };
        g.AddInvite(2);
        Assert.False(g.AddInvite(2));   // дубль
        Assert.True(g.AddMember(2, "R"));
        Assert.False(g.AddMember(2, "R")); // уже в группе
    }

    [Fact]
    public void Registry_tracks_invite_only_group_by_char_for_lookup()
    {
        var reg = new GroupRegistry();
        var g = reg.CreateInviteOnly(leaderGuid: 10, leaderName: "L");
        g.AddInvite(20);
        reg.TrackInvite(g, 20);

        Assert.Same(g, reg.GetByChar(20));
        Assert.Same(g, reg.GetByChar(10));
    }

    [Fact]
    public void Registry_detach_removes_char_only()
    {
        var reg = new GroupRegistry();
        var g = reg.CreateInviteOnly(10, "L");
        g.AddInvite(20);
        reg.TrackInvite(g, 20);

        reg.DetachChar(20);

        Assert.Null(reg.GetByChar(20));
        Assert.Same(g, reg.GetByChar(10));
    }

    [Fact]
    public void Registry_remove_clears_all_char_mappings()
    {
        var reg = new GroupRegistry();
        var g = reg.CreateInviteOnly(10, "L");
        g.AddMember(10, "L");
        g.AddMember(11, "M1");
        reg.OnMemberJoined(g, 11);
        g.AddInvite(12);
        reg.TrackInvite(g, 12);

        reg.Remove(g);

        Assert.Null(reg.GetByChar(10));
        Assert.Null(reg.GetByChar(11));
        Assert.Null(reg.GetByChar(12));
        Assert.Null(reg.GetById(g.Id));
    }
}
