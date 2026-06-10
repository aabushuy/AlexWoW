using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Системные сообщения в чат игрока (CHAT_MSG_SYSTEM): ответы dev-команд, подсказки игровых хендлеров
/// (гейт навыка сбора). DI-сервис (M7 S8, бывший статик <c>Dev.DevChat</c>); байты пакета — <see cref="ChatPackets"/>.
/// </summary>
internal sealed class ChatNotifier
{
    public Task SendSystemAsync(WorldSession session, string text, CancellationToken ct)
        => session.SendAsync(WorldOpcode.SmsgMessageChat, ChatPackets.BuildSystemMessage(text), ct);
}
