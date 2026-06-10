using AlexWoW.WorldServer.Net;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>Контекст выполнения dev-команды: сессия, аргументы (после имени команды) и ответ в чат.</summary>
public sealed class DevCommandContext(WorldSession session, IReadOnlyList<string> args)
{
    public WorldSession Session { get; } = session;

    /// <summary>Аргументы команды (токены после имени), как ввёл пользователь.</summary>
    public IReadOnlyList<string> Args { get; } = args;

    /// <summary>Аргумент по индексу нижним регистром, либо <paramref name="fallback"/> (по умолчанию пусто).</summary>
    public string ArgLower(int index, string fallback = "")
        => index < Args.Count ? Args[index].ToLowerInvariant() : fallback;

    /// <summary>Системный ответ игроку в чат (CHAT_MSG_SYSTEM).</summary>
    public Task ReplyAsync(string text, CancellationToken ct) => DevChat.ReplyAsync(Session, text, ct);
}
