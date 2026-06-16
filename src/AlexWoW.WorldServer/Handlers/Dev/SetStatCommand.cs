namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// §178 (Доработка А) <c>.setstat &lt;key&gt; &lt;value&gt;</c> — задать вторичную характеристику из редактора
/// аддона (крит/уклон/броня/оружие…). Ключи и кламп — <see cref="DevStatsCatalog"/>. После записи пушит
/// обновлённый кадр <c>stats</c> в аддон (окно-редактор показывает актуальное значение). Команда приходит
/// SAY-чатом от аддона (как и прочие dev-команды).
/// </summary>
internal sealed class SetStatCommand(DevStatsCatalog stats, AddonProtocol addon) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["setstat"];
    public string Help => ".setstat <key> <value>";
    public int Order => 63; // рядом с .setcrit (62)
    public bool RequiresWorld => false;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count < 2)
        {
            await ctx.ReplyAsync("Использование: .setstat <key> <value>", ct);
            return;
        }
        if (!stats.TrySet(ctx.Session, ctx.Args[0], ctx.Args[1], out var label))
        {
            await ctx.ReplyAsync($"Неизвестный стат или значение: {ctx.Args[0]}", ct);
            return;
        }
        await ctx.ReplyAsync($"{label} = {ctx.Args[1]}", ct);
        await addon.SendStatsAsync(ctx.Session, ct); // пуш обновлённого кадра → окно-редактор обновится
    }
}
