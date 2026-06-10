namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.help</c> / <c>.commands</c> — список команд (собирается из реестра). Себя не показывает.</summary>
internal sealed class HelpCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["help", "commands"];
    public string Help => string.Empty;   // не показываем .help в самом списке
    public int Order => 140;
    public bool RequiresWorld => false;

    public Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var list = string.Join(" | ", DevCommandRegistry.Ordered
            .Select(c => c.Help)
            .Where(h => !string.IsNullOrEmpty(h)));
        return ctx.ReplyAsync("Команды: " + list, ct);
    }
}
