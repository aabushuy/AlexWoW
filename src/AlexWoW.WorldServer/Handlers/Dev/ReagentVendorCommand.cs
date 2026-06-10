namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.reagentvendor</c> / <c>.rvendor</c> — поставить вендора реагентов у игрока (только 1, повтор
/// заменяет); <c>… off</c> — снять. Покупка — через существующий VendorHandlers (резолв по entry из GUID). D4.
/// </summary>
internal sealed class ReagentVendorCommand : IDevCommand
{
    /// <summary>Entry вендора реагентов «Tradesman Kontor» (Trade Supplies, NpcFlags=128 без госсипа —
    /// окно торговли открывается сразу). Продаёт нитки/флюс/краску/флаконы/пергамент/инструменты.</summary>
    private const uint ReagentVendorEntry = 27021;

    public IReadOnlyList<string> Names { get; } = ["reagentvendor", "rvendor"];
    public string Help => ".reagentvendor [off]";
    public int Order => 120;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var session = ctx.Session;
        if (ctx.ArgLower(0) == "off")
        {
            var removed = await session.World.DespawnDevNpcAsync(session, World.DevSlot.ReagentVendor, ct);
            await ctx.ReplyAsync(removed ? "Вендор реагентов снят" : "Вендор реагентов не поставлен", ct);
            return;
        }

        var ok = await session.World.SummonDevNpcAsync(session, ReagentVendorEntry, World.DevSlot.ReagentVendor, ct);
        await ctx.ReplyAsync(ok ? "Вендор реагентов поставлен" : "Не удалось поставить вендора", ct);
    }
}
