namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.cast SPELL</c> — кастует спелл на текущей цели игрока или на себя, если цели нет.
/// Стандартный путь каста (мана/КД/реагенты/каст-тайм работают как в игре). M-debug.</summary>
internal sealed class CastCommand(SpellCastService spellCast) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["cast"];
    public string Help => ".cast SPELL";
    public int Order => 42;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count < 1 || !uint.TryParse(ctx.Args[0], out var spellId))
        {
            await ctx.ReplyAsync("Использование: .cast SPELL", ct);
            return;
        }

        // SelectionGuid=0 → каст на себя; стандартный self-cast в WoW передаёт targetGuid=caster.
        var target = ctx.Session.Combat.SelectionGuid;
        if (target == 0)
            target = ctx.Session.InWorldGuid;

        await spellCast.StartCastAsync(ctx.Session, spellId, castCount: 0, targetGuid: target, ct);
        await ctx.ReplyAsync($"Cast {spellId} → 0x{target:X16}", ct);
    }
}
