namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.learnall</c> — выучить все доступные по уровню абилки у ближайшего классового тренера. M10.1.</summary>
internal sealed class LearnAllCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["learnall"];
    public string Help => ".learnall";
    public int Order => 50;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var n = await ctx.Session.TrainerCatalog.LearnAllFromNearbyTrainerAsync(ctx.Session, ct); // мост сессии (до S8)
        await ctx.ReplyAsync(n < 0
            ? "Рядом нет подходящего классового тренера"
            : $"Выучено абилок: {n}", ct);
    }
}
