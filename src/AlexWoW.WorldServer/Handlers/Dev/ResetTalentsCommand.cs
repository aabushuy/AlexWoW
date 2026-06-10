namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.resettalents</c> — бесплатно сбросить таланты (для теста перекидки). Переиспользует
/// wipe-логику тренера с нулевой стоимостью. Devcommands D5 (#70).</summary>
internal sealed class ResetTalentsCommand(TalentHandlers talents) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["resettalents", "untalent"];
    public string Help => ".resettalents";
    public int Order => 85;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        await talents.ResetTalentsAsync(ctx.Session, 0, ct);
        await ctx.ReplyAsync("Таланты сброшены (бесплатно)", ct);
    }
}
