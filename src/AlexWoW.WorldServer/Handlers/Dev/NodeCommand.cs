namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.node &lt;тип&gt;|&lt;entry&gt;|off</c> — поставить ноду сбора (рудная жила/трава) у игрока для теста
/// сбора профессий (M11.4). Реюз каркаса dev-GO (слот <see cref="World.DevSlot.Node"/>). Использование
/// ноды (правый клик) обрабатывает <see cref="GameObjectUseHandlers"/>.
/// </summary>
internal sealed class NodeCommand : IDevCommand
{
    private static readonly Dictionary<string, uint> NodeGo = new()
    {
        ["copper"] = 1731,
        ["tin"] = 1732,
        ["silver"] = 1733,
        ["iron"] = 1735,
        ["silverleaf"] = 1617,
        ["peacebloom"] = 1618,
        ["earthroot"] = 1619,
        ["mageroyal"] = 1620,
    };

    public IReadOnlyList<string> Names { get; } = ["node"];
    public string Help => ".node copper|tin|silver|iron|peacebloom|silverleaf|earthroot|mageroyal|<entry>|off";
    public int Order => 112;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var session = ctx.Session;
        var arg = ctx.ArgLower(0);

        if (arg == "off")
        {
            var removed = await session.World.DespawnDevGoAsync(session, World.DevSlot.Node, ct);
            await ctx.ReplyAsync(removed ? "Нода снята" : "Ноды нет", ct);
            return;
        }
        if (!NodeGo.TryGetValue(arg, out var entry) && !uint.TryParse(arg, out entry))
        {
            await ctx.ReplyAsync("Нода: copper/tin/silver/iron/peacebloom/silverleaf/earthroot/mageroyal/<entry> (или off)", ct);
            return;
        }

        var ok = await session.World.SummonDevGoAsync(session, entry, World.DevSlot.Node, ct);
        await ctx.ReplyAsync(ok ? $"Нода '{arg}' поставлена — кликни по ней правой кнопкой" : "Не удалось поставить ноду", ct);
    }
}
