using AlexWoW.WorldServer.Handlers;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Tests;

/// <summary>GROUP.T3/T4/T5: смена лидера, групповой XP rate, sub-group/raid конверсия, target icons.</summary>
public sealed class GroupT3T4T5Tests
{
    [Fact]
    public void ChangeLeader_moves_leadership_to_member()
    {
        var g = new Group { Id = 1, LeaderGuid = 0x10, LeaderName = "L" };
        g.AddMember(0x10, "L");
        g.AddMember(0x20, "M");

        var name = g.ChangeLeader(0x20);

        Assert.Equal("M", name);
        Assert.Equal(0x20UL, g.LeaderGuid);
        Assert.Equal("M", g.LeaderName);
    }

    [Fact]
    public void ChangeLeader_to_non_member_returns_null()
    {
        var g = new Group { Id = 1, LeaderGuid = 0x10, LeaderName = "L" };
        g.AddMember(0x10, "L");
        Assert.Null(g.ChangeLeader(0x99));
        Assert.Equal(0x10UL, g.LeaderGuid);
    }

    [Fact]
    public void PickOnlineHeir_skips_leader_and_offline()
    {
        var g = new Group { Id = 1, LeaderGuid = 0x10, LeaderName = "L" };
        g.AddMember(0x10, "L");
        g.AddMember(0x20, "Off");
        g.AddMember(0x30, "Online");
        g.Members[1].IsOnline = false;

        var heir = g.PickOnlineHeirExceptLeader();

        Assert.NotNull(heir);
        Assert.Equal(0x30UL, heir!.Guid);
    }

    [Theory]
    [InlineData(1, 1.0f)]
    [InlineData(2, 1.0f)]
    [InlineData(3, 1.166f)]
    [InlineData(4, 1.3f)]
    [InlineData(5, 1.4f)]
    [InlineData(10, 0.5f)]   // raid
    [InlineData(25, 0.5f)]
    public void GroupXpRate_matches_cmangos_breakdown(int memberCount, float expected)
    {
        Assert.Equal(expected, KillRewardService.GroupXpRate(memberCount), precision: 3);
    }

    [Fact]
    public void TargetIcons_init_to_zero()
    {
        var g = new Group { Id = 1, LeaderGuid = 0x10, LeaderName = "L" };
        Assert.Equal(8, g.TargetIcons.Length);
        Assert.All(g.TargetIcons, guid => Assert.Equal(0UL, guid));
    }

    [Fact]
    public void Type_switch_party_to_raid_changes_max_size()
    {
        var g = new Group { Id = 1, LeaderGuid = 0x10, LeaderName = "L" };
        Assert.Equal(5, g.MaxSize);
        g.Type = GroupType.Raid;
        Assert.Equal(40, g.MaxSize);
    }

    [Fact]
    public void Member_assistant_and_subgroup_can_be_set()
    {
        var g = new Group { Id = 1, LeaderGuid = 0x10, LeaderName = "L" };
        g.AddMember(0x20, "M");
        var m = g.Members[0];

        m.IsAssistant = true;
        m.SubGroup = 3;

        Assert.True(m.IsAssistant);
        Assert.Equal((byte)3, m.SubGroup);
    }
}
