namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.dummy [heal|healer|attack|caster|hunter|damage]</c> — переместить манекен к игроку.
/// #29; M12: лечебный (харнес, 990021); Ф2 #14: healer (скромный HP+самослив), hunter (стрельба), caster=маг (+баффы).</summary>
internal sealed class DummyCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["dummy"];
    public string Help => ".dummy [heal|healer|attack|caster|hunter|damage]";
    public int Order => 80;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (ctx.ArgLower(0) == "heal")
        {
            await ctx.Session.World.SummonHealDummyAsync(ctx.Session, ct);
            await ctx.ReplyAsync("Лечебный манекен (харнес) перемещён к вам (ранен — лечите его)", ct);
            return;
        }
        if (ctx.ArgLower(0) == "healer")
        {
            await ctx.Session.World.SummonHealerDummyAsync(ctx.Session, ct);
            await ctx.ReplyAsync("Лечебный манекен перемещён к вам (70% HP, самослив — лечите быстрее слива)", ct);
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
            await ctx.ReplyAsync("Манекен-маг перемещён к вам (кастует + баффы Int/Sta/метка — прерывайте/диспелайте)", ct);
            return;
        }
        if (ctx.ArgLower(0) == "hunter")
        {
            await ctx.Session.World.SummonHunterDummyAsync(ctx.Session, ct);
            await ctx.ReplyAsync("Манекен-охотник перемещён к вам (атакуйте — ответит выстрелами на расстоянии)", ct);
            return;
        }
        await ctx.Session.World.SummonTrainingDummyAsync(ctx.Session, ct);
        await ctx.ReplyAsync("Тренировочный манекен (урон) перемещён к вам", ct);
    }
}
