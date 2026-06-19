using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Tests;

/// <summary>GROUP.T2: пакеты SMSG_GROUP_LIST и SMSG_PARTY_MEMBER_STATS — байтовое представление.</summary>
public sealed class GroupPacketsTests
{
    [Fact]
    public void GroupList_party_two_members_basic_shape()
    {
        var g = new Group { Id = 7, LeaderGuid = 0x10, LeaderName = "Leader" };
        g.AddMember(0x10, "Leader");
        g.AddMember(0x20, "Other");

        var leader = g.Members.First(m => m.Guid == 0x10);
        var bytes = GroupPackets.BuildGroupList(g, leader);

        // Минимальные ожидания: первый байт = groupFlags (0 для party), 16 байт смещение должен включать leader guid.
        Assert.Equal(GroupPackets.GroupFlagNormal, bytes[0]);
        // 1 (flags) + 1 (subgroup) + 1 (memberFlags) + 1 (lfgRoles) = 4 байта шапки до groupGuid (no LFG).
        Assert.True(bytes.Length > 32, "Должна быть хотя бы шапка + один член + leaderGuid + lootBlock");
    }

    [Fact]
    public void GroupList_counter_increments_per_send()
    {
        var g = new Group { Id = 1, LeaderGuid = 0x10, LeaderName = "L" };
        g.AddMember(0x10, "L");
        g.AddMember(0x20, "M");

        var b1 = GroupPackets.BuildGroupList(g, g.Members[0]);
        var b2 = GroupPackets.BuildGroupList(g, g.Members[0]);

        // counter лежит после groupFlags(1)+subgroup(1)+memberFlags(1)+lfgRoles(1)+groupGuid(8) = байт 12.
        var c1 = System.BitConverter.ToUInt32(b1, 12);
        var c2 = System.BitConverter.ToUInt32(b2, 12);
        Assert.Equal(c1 + 1u, c2);
    }

    [Fact]
    public void PartyMemberStats_emits_only_requested_fields()
    {
        var flagsOnly = GroupPackets.GroupUpdateFlag.Status;
        var bytes = GroupPackets.BuildPartyMemberStats(memberGuid: 0x10,
            flags: flagsOnly,
            status: GroupPackets.MemberStatus.Online | GroupPackets.MemberStatus.Pvp,
            curHp: 999, maxHp: 999, powerType: 1, curPower: 50, maxPower: 100,
            level: 60, zone: 14, posX: 100, posY: 200);

        // Packed-GUID (1 + 1 байт) + uint32 flags (4) + uint16 status (2) = 8 байт.
        Assert.Equal(8, bytes.Length);
    }

    [Fact]
    public void PartyMemberStats_full_snapshot_size_matches_field_widths()
    {
        var flags = GroupPackets.GroupUpdateFlag.Status
                  | GroupPackets.GroupUpdateFlag.CurHp | GroupPackets.GroupUpdateFlag.MaxHp
                  | GroupPackets.GroupUpdateFlag.PowerType
                  | GroupPackets.GroupUpdateFlag.CurPower | GroupPackets.GroupUpdateFlag.MaxPower
                  | GroupPackets.GroupUpdateFlag.Level | GroupPackets.GroupUpdateFlag.Zone
                  | GroupPackets.GroupUpdateFlag.Position;

        var bytes = GroupPackets.BuildPartyMemberStats(0x10, flags,
            GroupPackets.MemberStatus.Online, 1000, 2000, 0, 50, 100, 60, 14, 123, 456);

        // PackedGuid(mask 1 байт + 1 ненулевой) + flags(4) + status(2) + curHp(4) + maxHp(4)
        // + powerType(1) + curPower(2) + maxPower(2) + level(2) + zone(2) + position(4) = 29
        Assert.Equal(29, bytes.Length);
    }
}
