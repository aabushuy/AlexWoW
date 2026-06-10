using AlexWoW.WorldServer.Net;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>Контекст выполнения dev-команды: сессия, аргументы (после имени команды) и ответ в чат.</summary>
internal sealed class DevCommandContext(
    WorldSession session,
    IReadOnlyList<string> args,
    ChatNotifier chat,
    IReadOnlyList<IDevCommand> allCommands)
{
    public WorldSession Session { get; } = session;

    /// <summary>Аргументы команды (токены после имени), как ввёл пользователь.</summary>
    public IReadOnlyList<string> Args { get; } = args;

    /// <summary>Все команды в порядке <see cref="IDevCommand.Order"/> — для <c>.help</c>. Передаётся через
    /// контекст, а не ctor-инъекцией реестра в команду: иначе DI-цикл
    /// (реестр ← IEnumerable&lt;IDevCommand&gt; ← HelpCommand ← реестр). M7 S8.</summary>
    public IReadOnlyList<IDevCommand> AllCommands { get; } = allCommands;

    /// <summary>Аргумент по индексу нижним регистром, либо <paramref name="fallback"/> (по умолчанию пусто).</summary>
    public string ArgLower(int index, string fallback = "")
        => index < Args.Count ? Args[index].ToLowerInvariant() : fallback;

    /// <summary>Системный ответ игроку в чат (CHAT_MSG_SYSTEM).</summary>
    public Task ReplyAsync(string text, CancellationToken ct) => chat.SendSystemAsync(Session, text, ct);
}
