using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Внебоевой реген HP (M6.7, DI-сервис M7 S4 — вынос из god-класса CombatHandlers): игрок — спустя 5 с
/// после боя ~10% макс. HP в секунду; существо — низким кадэнсом, повреждённое и не в бою.
/// Зовётся из серверного тика (<see cref="World.WorldTick.UpdateAsync"/>, рядом с реген-маны M6.4).
/// </summary>
internal sealed class RegenService
{
    /// <summary>Задержка после боя до старта внебоевого регена HP игрока (мс).</summary>
    private const long OutOfCombatRegenDelayMs = 5000;
    /// <summary>Кадэнс регена HP игрока (мс).</summary>
    private const long HealthRegenIntervalMs = 1000;

    /// <summary>
    /// Внебоевой реген HP игрока (M6.7): спустя 5 с после последней боевой активности восстанавливает
    /// ~10% макс. HP в секунду до полного. Зовётся из серверного тика (рядом с реген-маны M6.4).
    /// </summary>
    internal async Task TickPlayerRegenAsync(WorldSession session, long now, CancellationToken ct)
    {
        if (session.InWorldGuid == 0 || session.Combat.IsDead || session.Combat.Health >= session.Combat.MaxHealth)
            return;
        if (now - session.Combat.LastCombatMs < OutOfCombatRegenDelayMs)       // ещё «в бою» — реген на паузе
            return;
        if (now - session.Combat.LastHealthRegenMs < HealthRegenIntervalMs)
            return;

        session.Combat.LastHealthRegenMs = now;
        session.Combat.Health = Math.Min(session.Combat.MaxHealth, session.Combat.Health + Math.Max(1u, session.Combat.MaxHealth / 10));
        if (session.Player is { } player)
            await session.World.BroadcastPlayerHealthAsync(player, ct);
    }

    /// <summary>Реген HP существа вне боя (если повреждено и стоит дома). Низкий кадэнс. M6.7.</summary>
    internal async Task TickRegenAsync(WorldState world, WorldCreature creature, long now, CancellationToken ct)
    {
        if (creature.Health >= creature.MaxHealth || now < creature.NextRegenMs)
            return;
        creature.NextRegenMs = now + 1000;
        creature.Health = Math.Min(creature.MaxHealth, creature.Health + Math.Max(1, creature.MaxHealth / 20));
        await world.BroadcastCreatureHealthAsync(creature, ct);
    }
}
