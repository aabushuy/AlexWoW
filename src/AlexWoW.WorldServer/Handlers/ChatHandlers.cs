using System.Text;
using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Handlers.Dev;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>Чат (M4): приём CMSG_MESSAGECHAT, эхо отправителю. (DI-модуль, M7 #36.)</summary>
internal sealed class ChatHandlers(AddonProtocol addonProtocol, DevCommandDispatcher devCommands) : IOpcodeHandlerModule
{
    private const uint ChatTypeWhisper = 0x07; // CMSG: перед сообщением идёт CString адресата

    [WorldOpcodeHandler(WorldOpcode.CmsgMessageChat)]
    public async Task OnMessageChat(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var type = reader.UInt32();        // тип чата клиента (say/yell/emote/whisper…)
        var language = reader.UInt32();    // язык; LANG_ADDON (0xFFFFFFFF) → addon-протокол
        if (type == ChatTypeWhisper)
            reader.CString();              // адресат (для addon-whisper — сам игрок, не нужен)

        // Devcommands #79: addon-сообщение (язык LANG_ADDON) — кастомный обмен (каталог dev-меню), в чат не идёт.
        if (language == AddonProtocol.LangAddon)
        {
            var addonMsg = reader.CString();          // тело: "PREFIX\tBODY"
            var tab = addonMsg.IndexOf('\t');
            if (tab >= 0)
                await addonProtocol.HandleAsync(session, addonMsg[..tab], addonMsg[(tab + 1)..], ct);
            return;
        }

        var rest = reader.Bytes(reader.Remaining);
        var len = rest.Length;
        while (len > 0 && rest[len - 1] == 0)
            len--;
        if (len == 0)
            return;
        var msg = rest[..len].ToArray(); // сырые байты — без перекодировки (кириллица ок)

        var text = Encoding.UTF8.GetString(msg);
        session.Logger.LogInformation("CHAT '{User}' type={Type}: {Msg}", session.Account, type, text);

        // M9.4: дев-команды (.level/.xp/.additem) — не уходят в чат.
        if (await devCommands.TryHandleAsync(session, text, ct))
            return;

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
