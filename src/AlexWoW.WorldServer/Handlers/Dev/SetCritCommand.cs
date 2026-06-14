namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.setcrit [0-100]</c> — задать шанс крита заклинаний (CRIT.1) И мили-крита (CRIT.2) в %. База спелл-крита 0;
/// <c>.setcrit 100</c> — все касты и удары критуют (наглядная проверка). Спелл-крит живёт сессию; мили-крит
/// перезапишется ближайшим RefreshMeleeAsync (смена экипировки/левел) — для проверки бей сразу после команды.
/// </summary>
internal sealed class SetCritCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["setcrit"];
    public string Help => ".setcrit [0-100] — шанс крита заклинаний и мили, %";
    public int Order => 62; // рядом с .buff/.debuff
    public bool RequiresWorld => false;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var pct = int.TryParse(ctx.ArgLower(0), out var n) ? Math.Clamp(n, 0, 100) : 100;
        ctx.Session.Cast.SpellCritChance = pct;
        ctx.Session.Combat.MeleeCritPct = pct; // CRIT.2: тот же % для мили-крита (до ближайшего RefreshMelee)
        await ctx.ReplyAsync($"Шанс крита (заклинания и мили): {pct}%", ct);
    }
}
