namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.combo [N]</c> — задать очки серии (combo points) на текущей цели (0..5; по умолчанию 5). Проверка
/// CP.1: убедиться, что очки видны на UI цели и шлются клиентом (<c>SMSG_UPDATE_COMBO_POINTS</c>).
/// Боевой набор/расход очков — генераторы/финишеры (CP.2/CP.3).
/// </summary>
internal sealed class ComboCommand(ComboPointService combo) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["combo"];
    public string Help => ".combo [0-5] — очки серии на цели (проверка CP.1)";
    public int Order => 86; // рядом с .spelltest (85)
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var target = ctx.Session.Combat.SelectionGuid;
        if (target == 0)
        {
            await ctx.ReplyAsync("Нет цели — выберите существо и повторите .combo", ct);
            return;
        }

        var points = byte.TryParse(ctx.ArgLower(0), out var n) ? Math.Min(n, ComboPointService.MaxComboPoints) : ComboPointService.MaxComboPoints;
        await combo.SetAsync(ctx.Session, target, points, ct);
        await ctx.ReplyAsync($"Очки серии на цели: {points}", ct);
    }
}
