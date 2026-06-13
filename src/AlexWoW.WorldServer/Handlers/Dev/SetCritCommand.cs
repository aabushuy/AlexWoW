namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.setcrit [0-100]</c> — задать шанс крита заклинаний в % (CRIT.1). База 0 (крит из статов пока не
/// моделируется); <c>.setcrit 100</c> — все касты критуют (наглядная проверка). Значение живёт сессию (сброс на релоге).
/// </summary>
internal sealed class SetCritCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["setcrit"];
    public string Help => ".setcrit [0-100] — шанс крита заклинаний, %";
    public int Order => 62; // рядом с .buff/.debuff
    public bool RequiresWorld => false;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var pct = int.TryParse(ctx.ArgLower(0), out var n) ? Math.Clamp(n, 0, 100) : 100;
        ctx.Session.Cast.SpellCritChance = pct;
        await ctx.ReplyAsync($"Шанс крита заклинаний: {pct}%", ct);
    }
}
