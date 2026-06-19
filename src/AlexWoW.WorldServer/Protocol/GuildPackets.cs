// Порт CMaNGOS-WoTLK: src/game/Guilds/GuildHandler.cpp + Guild.cpp (SendCommandResult/SendEventLog/...)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Guilds/. GPL-2.0.

using System.Text;
using AlexWoW.Common.Network;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>Билдеры пакетов гильдии (3.3.5a): чистые функции аргументы → байты.</summary>
internal static class GuildPackets
{
    /// <summary>
    /// SMSG_GUILD_COMMAND_RESULT (0x093) — ответ на CMSG_GUILD_*: тип команды + имя цели + код результата.
    /// </summary>
    public static byte[] BuildCommandResult(GuildCommandType cmd, string targetName, GuildCommandError err)
    {
        var nameBytes = Encoding.UTF8.GetBytes(targetName);
        return new ByteWriter(4 + nameBytes.Length + 1 + 4)
            .UInt32((uint)cmd)
            .Bytes(nameBytes).UInt8(0)
            .UInt32((uint)err)
            .ToArray();
    }

    /// <summary>SMSG_GUILD_INVITE — уведомление приглашённому: имя приглашающего + имя гильдии.</summary>
    public static byte[] BuildGuildInvite(string inviterName, string guildName)
    {
        var inviter = Encoding.UTF8.GetBytes(inviterName);
        var guild = Encoding.UTF8.GetBytes(guildName);
        return new ByteWriter(inviter.Length + guild.Length + 2)
            .Bytes(inviter).UInt8(0)
            .Bytes(guild).UInt8(0)
            .ToArray();
    }

    /// <summary>SMSG_GUILD_DECLINE — уведомление инициатору об отказе.</summary>
    public static byte[] BuildGuildDecline(string declinerName)
    {
        var nameBytes = Encoding.UTF8.GetBytes(declinerName);
        return new ByteWriter(nameBytes.Length + 1).Bytes(nameBytes).UInt8(0).ToArray();
    }

    /// <summary>
    /// SMSG_GUILD_EVENT (0x092) — событие гильдии: тип + строки (variadic) + опционально guid.
    /// CMaNGOS: u8 event, u8 strCount, [CString * count], (u64 guid если нужно).
    /// </summary>
    public static byte[] BuildGuildEvent(GuildEvent ev, string[] strings, ulong? guid = null)
    {
        var w = new ByteWriter(64);
        w.UInt8((byte)ev);
        w.UInt8((byte)strings.Length);
        foreach (var s in strings)
        {
            var b = Encoding.UTF8.GetBytes(s);
            w.Bytes(b).UInt8(0);
        }
        if (guid is { } g)
            w.UInt64(g);
        return w.ToArray();
    }

    /// <summary>
    /// SMSG_GUILD_QUERY_RESPONSE (0x055): id, name, [10 rank names], emblem (style/color/border/border_color/bg).
    /// </summary>
    public static byte[] BuildGuildQueryResponse(Guild guild)
    {
        var w = new ByteWriter(256);
        w.UInt32(guild.PersistedId != 0 ? guild.PersistedId : guild.Id);
        var name = Encoding.UTF8.GetBytes(guild.Name);
        w.Bytes(name).UInt8(0);

        // 10 rank names (если меньше 10 — пустые строки).
        for (var i = 0; i < 10; i++)
        {
            var rank = i < guild.Ranks.Count ? guild.Ranks[i] : null;
            if (rank is not null)
            {
                var rn = Encoding.UTF8.GetBytes(rank.Name);
                w.Bytes(rn).UInt8(0);
            }
            else
            {
                w.UInt8(0);
            }
        }

        w.UInt32(guild.EmblemStyle)
         .UInt32(guild.EmblemColor)
         .UInt32(guild.BorderStyle)
         .UInt32(guild.BorderColor)
         .UInt32(guild.BackgroundColor);
        return w.ToArray();
    }

    /// <summary>
    /// SMSG_GUILD_ROSTER (0x08A): MOTD + GINFO + ranks (rights+limits) + members (name/rank/lvl/class/zone/notes).
    /// CMaNGOS Guild::Roster. <paramref name="canViewOfficerNote"/>=true → officer notes отдаются; иначе пусто.
    /// </summary>
    public static byte[] BuildGuildRoster(Guild guild, bool canViewOfficerNote)
    {
        var w = new ByteWriter(256 + guild.MemberCount * 64);
        w.UInt32((uint)guild.MemberCount);
        var motd = Encoding.UTF8.GetBytes(guild.Motd);
        w.Bytes(motd).UInt8(0);
        var ginfo = Encoding.UTF8.GetBytes(guild.InfoText);
        w.Bytes(ginfo).UInt8(0);

        const int BankTabs = 6;
        w.UInt32((uint)guild.Ranks.Count);
        foreach (var rank in guild.Ranks)
        {
            w.UInt32((uint)rank.Rights);
            w.UInt32((uint)rank.BankMoneyPerDay); // -1 = без лимита (отправляем как 0xFFFFFFFF)
            for (var t = 0; t < BankTabs; t++)
            {
                w.UInt32(0); // TabRight[i] — T4.1
                w.UInt32(0); // TabSlotPerDay[i] — T4.1
            }
        }

        var now = DateTime.UtcNow;
        foreach (var m in guild.Members)
        {
            w.UInt64(m.Guid);
            w.UInt8(m.IsOnline ? (byte)1 : (byte)0);
            var nm = Encoding.UTF8.GetBytes(m.Name);
            w.Bytes(nm).UInt8(0);
            w.UInt32(m.RankId);
            w.UInt8(m.Level);
            w.UInt8(m.Class);
            w.UInt8(0); // gender — у нас не критично, T4
            w.UInt32(m.Zone);
            if (!m.IsOnline)
            {
                // offline: float days since last logout (CMaNGOS).
                var days = m.LastLogoutAt is { } t ? (float)(now - t).TotalDays : 0f;
                w.Single(days);
            }
            var pnote = Encoding.UTF8.GetBytes(m.PublicNote);
            w.Bytes(pnote).UInt8(0);
            var onote = canViewOfficerNote ? Encoding.UTF8.GetBytes(m.OfficerNote) : [];
            w.Bytes(onote).UInt8(0);
        }
        return w.ToArray();
    }

    /// <summary>SMSG_GUILD_INFO (0x088): имя + created date + member counts.</summary>
    public static byte[] BuildGuildInfo(Guild guild)
    {
        var name = Encoding.UTF8.GetBytes(guild.Name);
        var onlineCount = (uint)guild.Members.Count(m => m.IsOnline);
        return new ByteWriter(name.Length + 1 + 4 + 4 + 4 + 4)
            .Bytes(name).UInt8(0)
            .UInt32((uint)guild.CreatedAt.Year - 1900)  // CMaNGOS отправляет (year-1900, month, day) тремя uint32 — упрощённо
            .UInt32((uint)guild.CreatedAt.Month)
            .UInt32((uint)guild.CreatedAt.Day)
            .UInt32((uint)guild.MemberCount + (onlineCount << 16))
            .ToArray();
    }
}
