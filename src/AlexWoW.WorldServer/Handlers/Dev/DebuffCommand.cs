namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.debuff SPELL [сек]</c> — наложить дебафф (отрицательную ауру) на себя (по умолчанию 120 с). Стенд для
/// проверки диспела (DSP.1): берите спелл с типом диспела (Magic/Curse/Disease/Poison) и снимайте его
/// Cleanse/Remove Curse/Dispel Magic. Параллель к <c>.buff</c>, но positive:false.
/// </summary>
internal sealed class DebuffCommand(AuraService auras) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["debuff"];
    public string Help => ".debuff SPELL [сек] — дебафф на себя (стенд для диспела)";
    public int Order => 61; // рядом с .buff (60)
    public bool RequiresWorld => false;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count < 1 || !uint.TryParse(ctx.Args[0], out var spell))
        {
            await ctx.ReplyAsync("Использование: .debuff SPELL [сек]", ct);
            return;
        }
        var secs = ctx.Args.Count >= 2 && uint.TryParse(ctx.Args[1], out var sv) ? sv : 120u;
        await auras.ApplyAsync(ctx.Session, spell, (int)(secs * 1000), positive: false, form: 0, ct);
        await ctx.ReplyAsync($"Дебафф {spell} на {secs}с (снимите диспелом нужного типа)", ct);
    }
}
