namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.xp [add] N</c> — добавить опыт. M9.4.</summary>
internal sealed class XpCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["xp"];
    public string Help => ".xp [add] N";
    public int Order => 20;
    public bool RequiresWorld => false;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        // Допускаем как ".xp 500", так и ".xp add 500".
        var idx = ctx.Args.Count >= 2 && ctx.Args[0].Equals("add", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (ctx.Args.Count <= idx || !uint.TryParse(ctx.Args[idx], out var amount))
        {
            await ctx.ReplyAsync("Использование: .xp [add] N", ct);
            return;
        }
        await Progression.GiveXpAsync(ctx.Session, amount, ct);
        await ctx.ReplyAsync($"Опыт +{amount}", ct);
    }
}
