namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.unbuff SPELL</c> — снять бафф. M6.11.</summary>
internal sealed class UnbuffCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["unbuff"];
    public string Help => ".unbuff SPELL";
    public int Order => 70;
    public bool RequiresWorld => false;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count < 1 || !uint.TryParse(ctx.Args[0], out var offSpell))
        {
            await ctx.ReplyAsync("Использование: .unbuff SPELL", ct);
            return;
        }
        await Auras.RemoveAsync(ctx.Session, offSpell, ct);
        await ctx.ReplyAsync($"Снят бафф {offSpell}", ct);
    }
}
