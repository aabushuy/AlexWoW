using System.Text;
using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Handlers.Dev;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Протокол «аддон ↔ сервер» (Devcommands #79). Транспорт — addon-сообщения поверх чат-опкодов 3.3.5a:
/// клиент шлёт <c>CMSG_MESSAGECHAT</c> с <c>language = LANG_ADDON (0xFFFFFFFF)</c> (тип WHISPER, тело
/// <c>"PREFIX\tBODY"</c>); сервер отвечает <c>SMSG_MESSAGECHAT</c> с тем же языком — клиент поднимает
/// событие <c>CHAT_MSG_ADDON</c> (в чат не печатается). Сверено с TrinityCore <c>Player::WhisperAddon</c>.
/// Сейчас единственная команда — <c>menu</c>: сервер отдаёт каталог dev-меню (<see cref="DevMenuCatalog"/>).
/// Не опкод-модуль (своих опкодов нет) — DI-сервис, инжектится в <see cref="ChatHandlers"/> (M7 #36).
/// Команды: <c>menu</c> — каталог dev-меню (<see cref="DevMenuCatalog"/>); <c>stats</c> — кадр вторичных
/// характеристик для окна-редактора (§178, <see cref="DevStatsCatalog"/>).
/// </summary>
internal sealed class AddonProtocol(DevMenuCatalog devMenu, DevStatsCatalog devStats)
{
    public const uint LangAddon = 0xFFFFFFFF;
    private const byte ChatMsgWhisper = 0x07; // тип чата для addon-сообщения (как в TrinityCore)

    /// <summary>Разобрать addon-сообщение от клиента (<paramref name="prefix"/>/<paramref name="body"/>).</summary>
    public async Task HandleAsync(WorldSession session, string prefix, string body, CancellationToken ct)
    {
        if (prefix != DevMenuCatalog.Prefix)
            return;

        if (body == "menu")
        {
            await SendMenuAsync(session, ct);
            return;
        }

        if (body == "stats")
        {
            await SendStatsAsync(session, ct);
            return;
        }

        session.Logger.LogDebug("ADDON '{User}': неизвестная команда '{Body}'", session.Account, body);
    }

    /// <summary>Отдать каталог dev-меню кадрами BEGIN … узлы … END. Не-админу — пустой каталог.</summary>
    private async Task SendMenuAsync(WorldSession session, CancellationToken ct)
    {
        await SendLineAsync(session, "BEGIN", ct);
        if (session.IsAdmin)
        {
            foreach (var line in await devMenu.BuildAsync(session, ct))
                await SendLineAsync(session, line, ct);
        }
        else
        {
            await SendLineAsync(session, "N|1|0|info|Доступно только администраторам", ct);
        }
        await SendLineAsync(session, "END", ct);
        session.Logger.LogDebug("ADDON '{User}': каталог dev-меню отправлен (admin={Admin})", session.Account, session.IsAdmin);
    }

    /// <summary>
    /// §178 (Доработка А) Отдать кадр вторичных характеристик для окна-редактора аддона: <c>SBEGIN</c> …
    /// <c>S|key|label|value</c> … <c>SEND</c>. Отдельные токены кадра (не BEGIN/END) — чтобы аддон не спутал
    /// его с каталогом меню (END меню вызывает перестроение дерева). Не-админу — ничего. Публичный: вызывается
    /// и по запросу <c>stats</c>, и пушем из <see cref="Dev.SetStatCommand"/> после записи (окно обновляется).
    /// </summary>
    public async Task SendStatsAsync(WorldSession session, CancellationToken ct)
    {
        if (!session.IsAdmin)
            return;
        await SendLineAsync(session, "SBEGIN", ct);
        foreach (var line in devStats.Build(session))
            await SendLineAsync(session, line, ct);
        await SendLineAsync(session, "SEND", ct);
    }

    private static Task SendLineAsync(WorldSession session, string line, CancellationToken ct)
        => session.SendAsync(WorldOpcode.SmsgMessageChat, BuildAddonMessage((ulong)session.InWorldGuid, line), ct);

    /// <summary>
    /// SMSG_MESSAGECHAT (3.3.5a) как addon-сообщение: u8 type(WHISPER) + u32 lang(ADDON) + u64 sender +
    /// u32 flags + u64 target (whisper → else-ветка) + SizedCString message(<c>"PREFIX\tline"</c>) + u8 tag.
    /// </summary>
    private static byte[] BuildAddonMessage(ulong guid, string line)
    {
        var payload = Encoding.UTF8.GetBytes(DevMenuCatalog.Prefix + "\t" + line);
        return new ByteWriter(40 + payload.Length)
            .UInt8(ChatMsgWhisper)
            .UInt32(LangAddon)
            .UInt64(guid)                       // sender (сам игрок)
            .UInt32(0)                          // flags
            .UInt64(guid)                       // target (whisper)
            .UInt32((uint)(payload.Length + 1)) // SizedCString: длина с нулём
            .Bytes(payload).UInt8(0)
            .UInt8(0)                           // chat tag
            .ToArray();
    }
}
