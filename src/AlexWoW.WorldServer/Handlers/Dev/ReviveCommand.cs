namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.revive</c> — мгновенно воскресить/долечить игрока до полного HP и маны. QA-удобство: после боя с
/// манекенами не нужно бегать к трупу. Делает то же, что обработчик CMSG_REPOP_REQUEST (возрождение на
/// месте), но командой и независимо от состояния (живой → просто долечивает).
/// </summary>
internal sealed class ReviveCommand(ManaRegenService manaRegen) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["revive"];
    public string Help => ".revive";
    public int Order => 64; // рядом с .setstat (63)
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var s = ctx.Session;
        s.Combat.IsDead = false;
        s.Combat.Health = s.Combat.MaxHealth;
        s.Cast.Mana = s.Cast.MaxMana;
        if (s.Player is { } player)
            await s.World.BroadcastPlayerHealthAsync(player, ct);
        await manaRegen.SendManaUpdateAsync(s, ct);
        await ctx.ReplyAsync("Воскрешён: полное HP/мана", ct);
    }
}
