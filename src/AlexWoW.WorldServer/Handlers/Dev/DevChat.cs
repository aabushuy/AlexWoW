using System.Text;
using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>Системные ответы dev-команд в чат игрока (CHAT_MSG_SYSTEM). Вынесено из DevCommands.</summary>
internal static class DevChat
{
    public static Task ReplyAsync(WorldSession session, string text, CancellationToken ct)
    {
        var msg = Encoding.UTF8.GetBytes(text);
        var w = new ByteWriter(40 + msg.Length)
            .UInt8(0)                       // CHAT_MSG_SYSTEM
            .UInt32(0)                      // LANG_UNIVERSAL
            .UInt64(0)                      // sender (система)
            .UInt32(0)                      // chat flags
            .UInt64(0)                      // target
            .UInt32((uint)(msg.Length + 1))
            .Bytes(msg).UInt8(0)
            .UInt8(0);                      // chat tag
        return session.SendAsync(WorldOpcode.SmsgMessageChat, w.ToArray(), ct);
    }
}
