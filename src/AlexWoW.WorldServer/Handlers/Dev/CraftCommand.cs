namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.craft anvil|forge|cookfire|mailbox</c> — поставить крафт-станок/почту (гейм-объект) у игрока
/// (один на тип, повтор заменяет); <c>.craft off</c> — снять все dev-станки. D3.
/// </summary>
internal sealed class CraftCommand : IDevCommand
{
    /// <summary>Крафт-станки/почта → канонические entry GO (anvil/forge/campfire — spell-focus type 8;
    /// mailbox — type 19; те же объекты, по которым клиент признаёт «вы рядом с …»).</summary>
    private static readonly Dictionary<string, uint> CraftGo = new()
    {
        ["anvil"] = 1744,      // Anvil (spell-focus 1)
        ["forge"] = 1685,      // Forge (spell-focus 3)
        ["cookfire"] = 1798,   // Campfire (spell-focus 4 — кулинария)
        ["mailbox"] = 32349,   // Mailbox (type 19)
    };

    public IReadOnlyList<string> Names { get; } = ["craft"];
    public string Help => ".craft anvil|forge|cookfire|mailbox|off";
    public int Order => 110;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var session = ctx.Session;
        var arg = ctx.ArgLower(0);

        if (arg == "off")
        {
            await session.World.DevCleanGosAsync(session, ct);
            await ctx.ReplyAsync("Dev-станки сняты", ct);
            return;
        }
        if (!CraftGo.TryGetValue(arg, out var entry))
        {
            await ctx.ReplyAsync("Станок: anvil/forge/cookfire/mailbox (или off)", ct);
            return;
        }

        var ok = await session.World.SummonDevGoAsync(session, entry, arg, ct);
        await ctx.ReplyAsync(ok ? $"Станок '{arg}' поставлен" : "Не удалось поставить станок", ct);
    }
}
