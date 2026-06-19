// Порт CMaNGOS-WoTLK: src/game/Groups/GroupHandler.cpp (SendPartyResult, SendGroupInvite)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Groups/GroupHandler.cpp. GPL-2.0.

using System.Text;
using AlexWoW.Common.Network;

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
}
