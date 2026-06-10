namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.level N</c> / <c>.lvl N</c> — установить уровень персонажа. M9.4.</summary>
internal sealed class LevelCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["level", "lvl"];
    public string Help => ".level N";
    public int Order => 10;
    public bool RequiresWorld => false;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count < 1 || !byte.TryParse(ctx.Args[0], out var lvl))
        {
            await ctx.ReplyAsync("Использование: .level N", ct);
            return;
        }
        await ctx.Session.Progression.SetLevelAsync(ctx.Session, lvl, ct); // мост сессии (до S8)
        await ctx.ReplyAsync($"Уровень: {Math.Clamp(lvl, (byte)1, World.LevelStore.MaxLevel)}", ct);
    }
}
