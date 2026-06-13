namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.rp [0-100]</c> — задать силу рун (runic power) DK для проверки RP-абилок (RUNE.4; по умолчанию 100).
/// Сила рун хранится ×10 (как ярость), поэтому значение умножается на 10. Аналог проверочных ресурс-команд.
/// </summary>
internal sealed class RunicPowerCommand(CombatResourcesService combatResources) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["rp"];
    public string Help => ".rp [0-100] — задать силу рун (runic power) DK (проверка RUNE.4)";
    public int Order => 88; // рядом с .runes (87)
    public bool RequiresWorld => true;

    private const byte PowerRunic = 6;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var value = byte.TryParse(ctx.ArgLower(0), out var n) ? Math.Min(n, (byte)100) : (byte)100;
        // Точная установка: обнуляем и добавляем (GainPowerAsync клампит к максимуму и шлёт полоску).
        ctx.Session.Combat.RunicPower = 0;
        await combatResources.GainPowerAsync(ctx.Session, PowerRunic, (uint)(value * 10), ct);
        await ctx.ReplyAsync($"Сила рун: {value}", ct);
    }
}
