using System.Text;
using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>Чат (M4): приём CMSG_MESSAGECHAT, эхо отправителю.</summary>
public static class ChatHandlers
{
    [WorldOpcodeHandler(WorldOpcode.CmsgMessageChat)]
    public static async Task OnMessageChat(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var type = reader.UInt32();        // тип чата клиента (say/yell/emote…)
        reader.UInt32();                   // язык — игнорируем
        var rest = reader.Bytes(reader.Remaining);
        var len = rest.Length;
        while (len > 0 && rest[len - 1] == 0)
            len--;
        if (len == 0)
            return;
        var msg = rest[..len].ToArray(); // сырые байты — без перекодировки (кириллица ок)

        session.Logger.LogInformation("CHAT '{User}' type={Type}: {Msg}",
            session.Account, type, Encoding.UTF8.GetString(msg));

        // Эхо отправителю (для одного игрока этого достаточно).
        var w = new ByteWriter(40 + msg.Length)
            .UInt8(1)                       // CHAT_MSG_SAY (display enum)
            .UInt32(0)                      // LANG_UNIVERSAL
            .UInt64(session.InWorldGuid)    // отправитель
            .UInt32(0)                      // chat flags
            .UInt64(0)                      // target
            .UInt32((uint)(msg.Length + 1))
            .Bytes(msg).UInt8(0)            // сообщение + null
            .UInt8(0);                      // chat tag
        await session.SendAsync(WorldOpcode.SmsgMessageChat, w.ToArray(), ct);
    }
}
