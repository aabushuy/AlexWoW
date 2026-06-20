namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// §178/Ф2 <c>.setstat &lt;key&gt; &lt;value&gt;</c> — задать характеристику из dev-панелей аддона
/// («Основное»/«Характеристики»). Ключи/кламп/группа — <see cref="DevStatsCatalog"/>. После записи пушит
/// нужное клиенту: первичные статы → <see cref="PeriodicsService"/>.SendStatsAsync (UNIT_FIELD_STAT + MaxHP/Mana);
/// ресурсы → реген-маны / <see cref="CombatResourcesService"/>.SendPowerAsync / broadcast HP. Вторичные —
/// только серверный combat-кэш (плюс старый кадр редактора). Команда приходит SAY-чатом от аддона.
/// </summary>
internal sealed class SetStatCommand(DevStatsCatalog stats, AddonProtocol addon, PeriodicsService periodics, ManaRegenService manaRegen) : IDevCommand
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
        if (!stats.TrySet(ctx.Session, ctx.Args[0], ctx.Args[1], out var label, out var push))
        {
            await ctx.ReplyAsync($"Неизвестный стат или значение: {ctx.Args[0]}", ct);
            return;
        }
        await ctx.ReplyAsync($"{label} = {ctx.Args[1]}", ct);

        var s = ctx.Session;
        switch (push)
        {
            case StatPush.Stats: await periodics.SendStatFieldsAsync(s, ct); break; // только UnitStat (без MaxHealth — иначе 1 HP)
            case StatPush.Health: if (s.Player is { } pl) await s.World.BroadcastPlayerHealthAsync(pl, ct); break;
            case StatPush.Mana: await manaRegen.SendManaUpdateAsync(s, ct); break;
            case StatPush.Rage: await CombatResourcesService.SendPowerAsync(s, 1, s.Combat.Rage, ct); break;       // powertype ярости
            case StatPush.Energy: await CombatResourcesService.SendPowerAsync(s, 3, s.Combat.Energy, ct); break;  // энергия
            case StatPush.Runic: await CombatResourcesService.SendPowerAsync(s, 6, s.Combat.RunicPower, ct); break; // рунич. сила
            default: await addon.SendStatsAsync(s, ct); break; // вторичные — обновить старый кадр редактора (если слушают)
        }
    }
}
