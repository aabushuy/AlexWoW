namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.dummy [heal|damage]</c> — переместить тренировочный манекен к игроку. #29; M12: лечебный манекен.</summary>
internal sealed class DummyCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["dummy"];
    public string Help => ".dummy [heal|damage]";
    public int Order => 80;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (ctx.ArgLower(0) == "heal")
        {
            await ctx.Session.World.SummonHealDummyAsync(ctx.Session, ct);
            await ctx.ReplyAsync("Лечебный манекен перемещён к вам (ранен — лечите его)", ct);
            return;
        }
        await ctx.Session.World.SummonTrainingDummyAsync(ctx.Session, ct);
        await ctx.ReplyAsync("Тренировочный манекен (урон) перемещён к вам", ct);
    }
}
