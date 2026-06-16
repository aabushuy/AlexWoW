namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.unbuff SPELL</c> — снять бафф; <c>.unbuff all</c> — снять все ауры (§176). M6.11.</summary>
internal sealed class UnbuffCommand(AuraService auras) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["unbuff"];
    public string Help => ".unbuff SPELL|all";
    public int Order => 70;
    public bool RequiresWorld => false;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        // §176: «.unbuff all» — снять все ауры игрока (вкл. формы/стойки — для dev-сценария уместно).
        if (string.Equals(ctx.ArgLower(0), "all", StringComparison.Ordinal))
        {
            // Снимок: RemoveAsync мутирует session.Progression.Auras, поэтому копируем spellId заранее.
            var spellIds = ctx.Session.Progression.Auras.Select(a => a.SpellId).ToList();
            foreach (var spellId in spellIds)
                await auras.RemoveAsync(ctx.Session, spellId, ct);
            await ctx.ReplyAsync($"Сняты все баффы ({spellIds.Count})", ct);
            return;
        }

        if (ctx.Args.Count < 1 || !uint.TryParse(ctx.Args[0], out var offSpell))
        {
            await ctx.ReplyAsync("Использование: .unbuff SPELL|all", ct);
            return;
        }
        await auras.RemoveAsync(ctx.Session, offSpell, ct);
        await ctx.ReplyAsync($"Снят бафф {offSpell}", ct);
    }
}
