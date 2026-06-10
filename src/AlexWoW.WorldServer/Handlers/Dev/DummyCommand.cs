namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.dummy</c> — переместить тренировочный манекен к игроку. #29.</summary>
internal sealed class DummyCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["dummy"];
    public string Help => ".dummy";
    public int Order => 80;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        await ctx.Session.World.SummonTrainingDummyAsync(ctx.Session, ct);
        await ctx.ReplyAsync("Тренировочный манекен перемещён к вам", ct);
    }
}
