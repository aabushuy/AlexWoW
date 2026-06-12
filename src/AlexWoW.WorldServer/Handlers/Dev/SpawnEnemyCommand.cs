namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.spawnenemy &lt;тип&gt; &lt;уровень&gt; [кол-во]</c> — спавнит враждебных существ заданного типа (CreatureType)
/// и уровня кольцом вокруг игрока (для проверки боя/абилок). Снимаются через <c>.devclean</c>. Меню «Враги»
/// (DevMenuCatalog) шлёт это как prompt: префикс «.spawnenemy &lt;тип&gt; », игрок вводит «уровень [кол-во]».
/// </summary>
internal sealed class SpawnEnemyCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["spawnenemy"];
    public string Help => ".spawnenemy <тип> <уровень> [кол-во]";
    public int Order => 85;
    public bool RequiresWorld => true;

    /// <summary>Имя типа (рус./англ.) → CreatureType (mangos).</summary>
    private static readonly Dictionary<string, byte> TypeByName = new()
    {
        ["beast"] = 1, ["животное"] = 1, ["зверь"] = 1,
        ["dragonkin"] = 2, ["дракон"] = 2,
        ["demon"] = 3, ["демон"] = 3,
        ["elemental"] = 4, ["элементаль"] = 4,
        ["giant"] = 5, ["великан"] = 5,
        ["undead"] = 6, ["нежить"] = 6,
        ["humanoid"] = 7, ["гуманоид"] = 7,
        ["mechanical"] = 9, ["механизм"] = 9,
    };

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (!TypeByName.TryGetValue(ctx.ArgLower(0), out var creatureType))
        {
            await ctx.ReplyAsync("Тип: humanoid/beast/demon/undead/dragonkin/elemental/giant/mechanical", ct);
            return;
        }
        var level = (byte)Math.Clamp(ParseInt(ctx.ArgLower(1), 80), 1, 83);
        var count = Math.Clamp(ParseInt(ctx.ArgLower(2), 1), 1, 20);
        var n = await ctx.Session.World.SpawnEnemiesAsync(ctx.Session, creatureType, level, count, ct);
        await ctx.ReplyAsync(n > 0
            ? $"Заспавнено врагов: {n} (ур.{level})"
            : "Существ такого типа не нашлось в БД мира", ct);
    }

    private static int ParseInt(string s, int fallback) => int.TryParse(s, out var v) ? v : fallback;
}
