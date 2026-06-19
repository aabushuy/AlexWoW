// Порт CMaNGOS-WoTLK: src/game/Groups/Group.cpp (SendUpdateTo, MEMBER_STATUS, GroupType flags)
// + GroupHandler.cpp (SendPartyResult, SendGroupInvite).
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Groups/. GPL-2.0.

using System.Text;
using AlexWoW.Common.Network;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Билдеры пакетов группы/партии (3.3.5a): чистые функции аргументы → байты.
/// </summary>
internal static class GroupPackets
{
    /// <summary>Операции для SMSG_PARTY_COMMAND_RESULT (CMaNGOS enum PartyOperation).</summary>
    public enum PartyOperation : uint
    {
        Invite = 0,
        UninviteOrLeave = 2,
        Swap = 4,
    }

    /// <summary>Коды результата для SMSG_PARTY_COMMAND_RESULT (CMaNGOS enum PartyResult).</summary>
    public enum PartyResult : uint
    {
        Ok = 0,
        BadPlayerNameS = 1,
        TargetNotInGroupS = 2,
        TargetNotInInstanceS = 3,
        GroupFull = 4,
        AlreadyInGroupS = 5,
        NotInGroup = 6,
        NotLeader = 7,
        PlayerWrongFaction = 8,
        IgnoringYouS = 9,
    }

    /// <summary>
    /// SMSG_PARTY_COMMAND_RESULT — ответ инициатору на CMSG_GROUP_INVITE/UNINVITE/...:
    /// успех или ошибка с подставкой имени цели.
    /// </summary>
    public static byte[] BuildPartyCommandResult(PartyOperation op, string memberName, PartyResult result)
    {
        var nameBytes = Encoding.UTF8.GetBytes(memberName);
        return new ByteWriter(4 + nameBytes.Length + 1 + 4 + 4)
            .UInt32((uint)op)
            .Bytes(nameBytes).UInt8(0)
            .UInt32((uint)result)
            .UInt32(0)                  // LFD cooldown (не используется без LFG)
            .ToArray();
    }

    /// <summary>
    /// SMSG_GROUP_INVITE — уведомление приглашённому. <paramref name="alreadyInGroup"/>=true,
    /// если получатель уже в другой группе (это inform-only, не invite).
    /// </summary>
    public static byte[] BuildGroupInvite(string inviterName, bool alreadyInGroup)
    {
        var nameBytes = Encoding.UTF8.GetBytes(inviterName);
        return new ByteWriter(1 + nameBytes.Length + 1 + 4 + 1 + 4)
            .UInt8(alreadyInGroup ? (byte)0 : (byte)1)
            .Bytes(nameBytes).UInt8(0)
            .UInt32(0)                  // unk
            .UInt8(0)                   // count instance saves
            .UInt32(0)                  // unk
            .ToArray();
    }

    /// <summary>SMSG_GROUP_DECLINE — уведомление инициатору о том, что получатель отказался.</summary>
    public static byte[] BuildGroupDecline(string declinerName)
    {
        var nameBytes = Encoding.UTF8.GetBytes(declinerName);
        return new ByteWriter(nameBytes.Length + 1)
            .Bytes(nameBytes).UInt8(0)
            .ToArray();
    }

    /// <summary>Биты GroupType из CMaNGOS Group.h — 0x02 raid, 0x10 destroyed (T3). Партия = 0.</summary>
    public const byte GroupFlagNormal = 0x00;
    public const byte GroupFlagRaid = 0x02;

    /// <summary>GROUP_MEMBER_FLAG_* — assistant/main-assist/main-tank (T5). T2 — все нули.</summary>
    public const byte MemberFlagNone = 0x00;

    /// <summary>MEMBER_STATUS из CMaNGOS Group.h — bitmask online/PVP/dead/AFK/...</summary>
    [System.Flags]
    public enum MemberStatus : byte
    {
        Offline = 0x00,
        Online = 0x01,
        Pvp = 0x02,
        Dead = 0x04,
        Ghost = 0x08,
        PvpFfa = 0x10,
        ZoneOut = 0x20,
        Afk = 0x40,
        Dnd = 0x80,
    }

    /// <summary>
    /// SMSG_GROUP_LIST (0x07D) — индивидуально для каждого получателя (его строка идёт в шапке, остальные — в списке).
    /// </summary>
    public static byte[] BuildGroupList(Group group, GroupMember receiver, byte receiverLfgRoles = 0)
    {
        var w = new ByteWriter(64 + group.MemberCount * 32)
            .UInt8(group.Type == GroupType.Raid ? GroupFlagRaid : GroupFlagNormal)
            .UInt8(receiver.SubGroup)
            .UInt8(receiver.IsAssistant ? (byte)0x01 : MemberFlagNone)
            .UInt8(receiverLfgRoles)
            // LFG-blob — пропускаем, у нас GROUP_FLAG_LFG не выставлен.
            .UInt64(0x1F40_0000_0000_0001UL | ((ulong)group.Id << 32)) // group GUID (HIGHGUID_GROUP = 0x1F40)
            .UInt32(group.NextCounter())
            .UInt32((uint)(group.MemberCount - 1));

        foreach (var m in group.Members)
        {
            if (m.Guid == receiver.Guid)
                continue;
            var nameBytes = Encoding.UTF8.GetBytes(m.Name);
            w.Bytes(nameBytes).UInt8(0)
             .UInt64(m.Guid)
             .UInt8((byte)MemberStatusFor(m))
             .UInt8(m.SubGroup)
             .UInt8(m.IsAssistant ? (byte)0x01 : MemberFlagNone)
             .UInt8(0); // lfg roles (T5/LFG)
        }

        w.UInt64(group.LeaderGuid);
        if (group.MemberCount > 1)
        {
            w.UInt8(group.LootMethod)
             .UInt64(group.LootMasterGuid)
             .UInt8(0x02)              // lootThreshold (LOOT_THRESHOLD_UNCOMMON по умолчанию)
             .UInt8(0)                 // dungeonDifficulty (Normal)
             .UInt8(0)                 // raidDifficulty (10-Normal)
             .UInt8(0);                // isDynamicHeroic
        }
        return w.ToArray();
    }

    /// <summary>Статус по члену группы (T2 — упрощённо: online/offline; PVP/dead/AFK/... — T3+).</summary>
    private static MemberStatus MemberStatusFor(GroupMember m)
        => m.IsOnline ? MemberStatus.Online : MemberStatus.Offline;

    /// <summary>Биты GROUP_UPDATE_FLAG_* (CMaNGOS Group.h enum GroupUpdateFlags). T2 — STATUS+HP+POWER+LEVEL+ZONE+POSITION.</summary>
    [System.Flags]
    public enum GroupUpdateFlag : uint
    {
        None = 0,
        Status = 0x0001,            // uint16
        CurHp = 0x0002,            // uint32
        MaxHp = 0x0004,            // uint32
        PowerType = 0x0008,            // uint8
        CurPower = 0x0010,            // uint16
        MaxPower = 0x0020,            // uint16
        Level = 0x0040,            // uint16
        Zone = 0x0080,            // uint16
        Position = 0x0100,            // uint16 x, uint16 y
        Pvp = Status,
    }

    /// <summary>
    /// SMSG_PARTY_MEMBER_STATS (0x07E) — частичный апдейт (биты в <paramref name="flags"/>);
    /// поля кладутся в порядке возрастания битов GroupUpdateFlag.
    /// PackedGuid — простая форма без оптимизации нулевых байт (для T2 достаточно).
    /// </summary>
    public static byte[] BuildPartyMemberStats(ulong memberGuid, GroupUpdateFlag flags,
        MemberStatus status, uint curHp, uint maxHp, byte powerType, ushort curPower, ushort maxPower,
        ushort level, ushort zone, ushort posX, ushort posY)
    {
        var w = new ByteWriter(64);
        PackedGuid.Write(w, memberGuid);
        w.UInt32((uint)flags);
        if ((flags & GroupUpdateFlag.Status) != 0) w.UInt16((ushort)status);
        if ((flags & GroupUpdateFlag.CurHp) != 0) w.UInt32(curHp);
        if ((flags & GroupUpdateFlag.MaxHp) != 0) w.UInt32(maxHp);
        if ((flags & GroupUpdateFlag.PowerType) != 0) w.UInt8(powerType);
        if ((flags & GroupUpdateFlag.CurPower) != 0) w.UInt16(curPower);
        if ((flags & GroupUpdateFlag.MaxPower) != 0) w.UInt16(maxPower);
        if ((flags & GroupUpdateFlag.Level) != 0) w.UInt16(level);
        if ((flags & GroupUpdateFlag.Zone) != 0) w.UInt16(zone);
        if ((flags & GroupUpdateFlag.Position) != 0) w.UInt16(posX).UInt16(posY);
        return w.ToArray();
    }
}
