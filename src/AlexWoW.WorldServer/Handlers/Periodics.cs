using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>Активный периодический эффект (DoT на существе / HoT на себе): тик урона/хила во времени. M10.4b.</summary>
public sealed class PeriodicEffect
{
    public uint SpellId;
    public ulong TargetGuid;   // GUID существа (DoT); 0 — сам игрок (HoT)
    public byte SchoolMask;
    public int Amount;         // величина за тик
    public int IntervalMs;
    public long NextTickMs;
    public long ExpiresAtMs;
    public bool IsHeal;
    public byte Slot;          // слот ауры на цели (для DoT-дебаффа)
    public bool OwnsVisual;    // true — мы шлём AURA_UPDATE на цель (DoT); HoT-визуал — в системе аур игрока
}

/// <summary>
/// Периодические ауры (M10.4b): DoT (урон по существу во времени) и HoT (хил себе). Тик в серверном цикле
/// (<see cref="World.WorldState.UpdateAsync"/>). DoT кладёт дебафф на существо (SMSG_AURA_UPDATE с реальным
/// кастером) и тикает урон (SMSG_PERIODICAURALOG); HoT использует бафф-иконку системы аур (M6.11) + тикает хил.
/// Величина/интервал/длительность — из spell_template (BasePoints+1, EffectAmplitude, SpellDuration.dbc).
/// </summary>
public static class Periodics
{
    /// <summary>Накладывает периодический эффект каста (после применения прямого эффекта). M10.4b.</summary>
    internal static async Task ApplyAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetCreatureGuid, CancellationToken ct)
    {
        if (!info.Periodic || info.AuraDurationMs <= 0 || session.InWorldGuid == 0)
            return;

        var interval = info.TickIntervalMs > 0 ? info.TickIntervalMs : 3000;
        var now = Environment.TickCount64;
        var expires = now + info.AuraDurationMs;
        var caster = (ulong)session.InWorldGuid;
        var level = (byte)(session.Character?.Level ?? 1);

        if (info.PeriodicHeal)
        {
            // HoT на себя: бафф-иконка — через систему аур (M6.11), тик хила — здесь.
            session.Periodics.RemoveAll(p => p.SpellId == spellId && p.TargetGuid == 0);
            await Auras.ApplyAsync(session, spellId, info.AuraDurationMs, positive: true, form: 0, ct);
            session.Periodics.Add(new PeriodicEffect
            {
                SpellId = spellId, TargetGuid = 0, SchoolMask = info.School, Amount = info.TickAmount,
                IntervalMs = interval, NextTickMs = now + interval, ExpiresAtMs = expires, IsHeal = true,
            });
            return;
        }

        // DoT на существо.
        var creature = targetCreatureGuid != 0 ? session.World.FindCreature(targetCreatureGuid) : null;
        if (creature is null || !creature.IsAlive)
            return;

        // Рефреш: снять прежний экземпляр того же DoT на этой цели (тот же слот).
        var dup = session.Periodics.FirstOrDefault(p => p.SpellId == spellId && p.TargetGuid == targetCreatureGuid);
        byte slot;
        if (dup is not null) { slot = dup.Slot; session.Periodics.Remove(dup); }
        else slot = (byte)session.Periodics.Count(p => p.TargetGuid == targetCreatureGuid);

        const byte flags = AuraFlags.Effect1 | AuraFlags.Negative | AuraFlags.Duration;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAuraUpdate,
            AuraPackets.BuildApplyByCaster(creature.Guid, caster, slot, spellId, flags, level, 1, info.AuraDurationMs), ct);
        session.Periodics.Add(new PeriodicEffect
        {
            SpellId = spellId, TargetGuid = targetCreatureGuid, SchoolMask = info.School, Amount = info.TickAmount,
            IntervalMs = interval, NextTickMs = now + interval, ExpiresAtMs = expires, IsHeal = false,
            OwnsVisual = true, Slot = slot,
        });
    }

    /// <summary>Тик периодических эффектов (из WorldState.UpdateAsync): применяет урон/хил, снимает истёкшие.</summary>
    internal static async Task TickAsync(WorldSession session, long now, CancellationToken ct)
    {
        if (session.Periodics.Count == 0 || session.InWorldGuid == 0)
            return;
        var caster = (ulong)session.InWorldGuid;

        foreach (var p in session.Periodics.ToList())
        {
            if (p.NextTickMs <= now && now < p.ExpiresAtMs + p.IntervalMs)
            {
                p.NextTickMs += p.IntervalMs;
                if (p.IsHeal)
                    await TickHealAsync(session, p, caster, ct);
                else
                    await TickDamageAsync(session, p, caster, now, ct);
            }
            if (now >= p.ExpiresAtMs && session.Periodics.Contains(p))
                await RemoveAsync(session, p, ct);
        }
    }

    private static async Task TickDamageAsync(WorldSession session, PeriodicEffect p, ulong caster, long now, CancellationToken ct)
    {
        var creature = session.World.FindCreature(p.TargetGuid);
        if (creature is null || !creature.IsAlive)
        {
            await RemoveAsync(session, p, ct);
            return;
        }
        session.LastCombatMs = now;
        var amount = (uint)Math.Max(1, p.Amount);
        var (_, _, died) = session.World.ApplyCreatureDamage(creature, amount);
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgPeriodicAuraLog,
            AuraPackets.BuildPeriodicLog(creature.Guid, caster, p.SpellId, isHeal: false, amount, p.SchoolMask), ct);
        await session.World.BroadcastCreatureHealthAsync(creature, ct);
        if (died)
        {
            await LootHandlers.OnCreatureKilledAsync(session, creature, ct);
            await RemoveAsync(session, p, ct);
        }
        else
            await CombatHandlers.EnsureCreatureRetaliationAsync(session, creature, roar: false, ct);
    }

    private static async Task TickHealAsync(WorldSession session, PeriodicEffect p, ulong caster, CancellationToken ct)
    {
        if (session.Player is not { } player)
            return;
        var before = session.Health;
        session.Health = Math.Min(session.MaxHealth, before + (uint)Math.Max(1, p.Amount));
        var effective = session.Health - before;
        await session.World.BroadcastToPlayerObserversAsync(player, WorldOpcode.SmsgPeriodicAuraLog,
            AuraPackets.BuildPeriodicLog(player.Guid, caster, p.SpellId, isHeal: true, effective, 0), ct);
        await session.World.BroadcastPlayerHealthAsync(player, ct);
    }

    private static async Task RemoveAsync(WorldSession session, PeriodicEffect p, CancellationToken ct)
    {
        session.Periodics.Remove(p);
        if (!p.OwnsVisual)
            return; // HoT-визуал (бафф-иконка) истечёт сам в Auras.TickAsync (та же длительность)
        var creature = session.World.FindCreature(p.TargetGuid);
        if (creature is not null)
            await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAuraUpdate,
                AuraPackets.BuildRemove(creature.Guid, p.Slot), ct);
    }
}
