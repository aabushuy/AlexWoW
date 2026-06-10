namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.learn SPELL</c> — выучить спелл без тренера (персист + грант, LEARNED/SUPERCEDED). M9.3/M10.3.</summary>
internal sealed class LearnCommand(SpellLearnService spellLearn) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["learn"];
    public string Help => ".learn SPELL";
    public int Order => 40;
    public bool RequiresWorld => false;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count < 1 || !uint.TryParse(ctx.Args[0], out var spellId))
        {
            await ctx.ReplyAsync("Использование: .learn SPELL", ct);
            return;
        }
        await spellLearn.GrantAsync(ctx.Session, spellId, ct);
        await ctx.ReplyAsync($"Изучен спелл {spellId}", ct);
    }
}
