namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.dummy [heal|attack|damage]</c> — переместить манекен к игроку. #29; M12: лечебный; защита: атакующий.</summary>
internal sealed class DummyCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["dummy"];
    public string Help => ".dummy [heal|attack|caster|damage]";
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
        if (ctx.ArgLower(0) == "attack")
        {
            await ctx.Session.World.SummonAttackDummyAsync(ctx.Session, ct);
            await ctx.ReplyAsync("Атакующий манекен перемещён к вам (атакуйте — он ответит; проверка защиты)", ct);
            return;
        }
        if (ctx.ArgLower(0) == "caster")
        {
            await ctx.Session.World.SummonCasterDummyAsync(ctx.Session, ct);
            await ctx.ReplyAsync("Кастующий манекен перемещён к вам (крутит каст — прерывайте Kick/Counterspell/Pummel)", ct);
            return;
        }
        await ctx.Session.World.SummonTrainingDummyAsync(ctx.Session, ct);
        await ctx.ReplyAsync("Тренировочный манекен (урон) перемещён к вам", ct);
    }
}
