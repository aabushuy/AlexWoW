namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.buff SPELL [сек]</c> — наложить бафф (по умолчанию 120 с). M6.11.</summary>
internal sealed class BuffCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["buff"];
    public string Help => ".buff SPELL [сек]";
    public int Order => 60;
    public bool RequiresWorld => false;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count < 1 || !uint.TryParse(ctx.Args[0], out var buffSpell))
        {
            await ctx.ReplyAsync("Использование: .buff SPELL [сек]", ct);
            return;
        }
        var secs = ctx.Args.Count >= 2 && uint.TryParse(ctx.Args[1], out var sv) ? sv : 120u;
        // мост (M7 S3): dev-команды — статиковый реестр, сервис аур достаём через сессию.
        await ctx.Session.AuraService.ApplyAsync(ctx.Session, buffSpell, (int)(secs * 1000), positive: true, form: 0, ct);
        await ctx.ReplyAsync($"Бафф {buffSpell} на {secs}с", ct);
    }
}
