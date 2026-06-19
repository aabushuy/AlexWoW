using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Tests;

/// <summary>GROUP.T6: восстановление группы из БД (RehydrateGroup) без сети и БД.</summary>
public sealed class GroupRecoveryTests
{
    [Fact]
    public void RehydrateGroup_sets_persisted_id_and_basic_state()
    {
        var reg = new GroupRegistry();
        var g = reg.RehydrateGroup(persistedId: 42, leaderGuid: 0x10, leaderName: "Boss",
            GroupType.Raid, lootMethod: 2, lootMasterGuid: 0x11);

        Assert.Equal(42u, g.PersistedId);
        Assert.NotEqual(0u, g.Id);          // in-memory id присвоен отдельно
        Assert.Equal(0x10UL, g.LeaderGuid);
        Assert.Equal("Boss", g.LeaderName);
        Assert.Equal(GroupType.Raid, g.Type);
        Assert.Equal((byte)2, g.LootMethod);
        Assert.Equal(0x11UL, g.LootMasterGuid);
        Assert.Same(g, reg.GetByChar(0x10));
        Assert.Same(g, reg.GetById(g.Id));
    }

    [Fact]
    public void RehydrateGroup_member_starts_offline_and_can_be_promoted_online()
    {
        var reg = new GroupRegistry();
        var g = reg.RehydrateGroup(1, 0x10, "L", GroupType.Party, 0, 0);
        g.AddMember(0x10, "L");
        g.Members[0].IsOnline = false;        // имитация recovery
        g.AddMember(0x20, "M");
        g.Members[1].IsOnline = false;
        reg.OnMemberJoined(g, 0x20);

        // Никто не онлайн — нет наследника.
        Assert.Null(g.PickOnlineHeirExceptLeader());

        // M пришёл онлайн.
        g.Members[1].IsOnline = true;
        var heir = g.PickOnlineHeirExceptLeader();
        Assert.NotNull(heir);
        Assert.Equal(0x20UL, heir!.Guid);
    }
}
