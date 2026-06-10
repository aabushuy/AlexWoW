namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.devclean</c> — снести ВСЕ dev-сущности (тренеры/станки/вендор). Манекен (<c>.dummy</c>)
/// не затронут. D1.</summary>
internal sealed class DevCleanCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["devclean"];
    public string Help => ".devclean";
    public int Order => 130;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        await ctx.Session.World.DevCleanAsync(ctx.Session, ct);
        await ctx.ReplyAsync("Все dev-сущности сняты", ct);
    }
}
