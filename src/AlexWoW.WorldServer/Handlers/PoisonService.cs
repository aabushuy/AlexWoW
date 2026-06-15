using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// §8 On-hit яды разбойника: пока активен яд-бафф (эксклюзивная группа <see cref="SpellCatalog.GroupRoguePoison"/>),
/// прошедший мили-свинг по шансу прокает природный урон (Instant/Deadly/Wound). Crippling — чистый слоу (не
/// моделируем). Яды наносятся использованием яда-предмета на оружие (CMSG_USE_ITEM → спелл-применение, эффект 54);
/// бафф вешает <see cref="SpellCastCompletion"/>. Зеркало <see cref="ImbueService"/>/<see cref="SealService"/>.
/// Упрощения: Deadly-стек DoT, Wound-дебафф лечения, Crippling-слоу не моделируются — все дают разовый природный урон.
/// </summary>
internal sealed class PoisonService(KillRewardService killReward, CrowdControlService crowdControl,
    ILogger<PoisonService> logger)
{
    // Ранги спеллов-применения ядов (эффект 54). Классификация по набору id (тип определяет прок).
    private static readonly HashSet<uint> Instant =
        [8679, 8686, 8688, 11338, 11339, 11340, 26891, 57967, 57968];
    private static readonly HashSet<uint> Deadly =
        [2823, 2824, 11355, 11356, 25351, 26967, 27186, 57972, 57973];
    private static readonly HashSet<uint> Wound =
        [13219, 13225, 13226, 13227, 27188, 57977, 57978];
    private static readonly HashSet<uint> Crippling = [3408, 11201];

    private const byte SchoolNature = 8; // SCHOOL_MASK_NATURE — школа ядов
    private const double ProcChance = 0.30; // шанс прока на удар (Instant) / наложения стека (Deadly)

    // Deadly Poison — стек-DoT: до 5 зарядов, тик каждые 3с в течение 12с, урон тика = базовый × число зарядов.
    private const byte DeadlyMaxStacks = 5;
    private const int DeadlyTickIntervalMs = 3000;
    private const int DeadlyDurationMs = 12000;

    // Crippling Poison — снара цели: −50% скорости бега на 12с (обновляется ударами).
    private const byte CripplingSlowPct = 50;
    private const int CripplingDurationMs = 12000;

    // Wound Poison — стек-дебафф снижения лечения цели: до 5 зарядов по −10% (=−50%), 15с.
    private const byte WoundMaxStacks = 5;
    private const int WoundReductionPerStack = 10;
    private const int WoundDurationMs = 15000;

    /// <summary>Является ли спелл нанесением яда разбойника (любой ранг Instant/Deadly/Wound/Crippling).</summary>
    internal static bool IsPoison(uint spellId)
        => Instant.Contains(spellId) || Deadly.Contains(spellId) || Wound.Contains(spellId) || Crippling.Contains(spellId);

    /// <summary>Прок активного яда по прошедшему свингу. Возвращает true, если цель умерла от прок-урона.</summary>
    internal async Task<bool> OnMeleeHitAsync(WorldSession session, WorldCreature creature, long now, CancellationToken ct)
    {
        var poison = session.Progression.Auras.FirstOrDefault(a => IsPoison(a.SpellId));
        if (poison is null)
            return false;

        // Crippling Poison — снара цели (−50% скорости, 12с); урона нет. Накладывается почти каждый удар.
        if (Crippling.Contains(poison.SpellId))
        {
            await crowdControl.ApplySnareAsync(session, creature, poison.SpellId, CripplingSlowPct, CripplingDurationMs, now, ct);
            return false;
        }
        // Wound Poison — стек-дебафф снижения лечения цели (до 5×10% = −50%); урона нет. По шансу +1 заряд.
        if (Wound.Contains(poison.SpellId))
        {
            if (Random.Shared.NextDouble() < ProcChance)
                await ApplyWoundStackAsync(session, creature, poison.SpellId, now, ct);
            return false;
        }

        var level = (byte)(session.Character?.Level ?? 1);

        // Deadly Poison — стек-DoT: по шансу добавляем заряд (до 5) и обновляем DoT на цели. Урон идёт тиками
        // (PeriodicsService.TickAsync), поэтому наложение само по себе не убивает (died=false).
        if (Deadly.Contains(poison.SpellId))
        {
            if (Random.Shared.NextDouble() < ProcChance)
                await ApplyDeadlyStackAsync(session, creature, poison.SpellId, level, now, ct);
            return false;
        }

        // Instant Poison — разовый природный урон по шансу.
        if (Random.Shared.NextDouble() >= ProcChance)
            return false;
        var amount = (uint)Random.Shared.Next(level, level * 2 + 1);
        var (_, overkill, died) = session.World.ApplyCreatureDamage(creature, amount);
        session.Combat.LastCombatMs = now;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgSpellNonMeleeDamageLog,
            SpellPackets.BuildDamageLog(creature.Guid, (ulong)session.InWorldGuid, poison.SpellId, amount, overkill, SchoolNature), ct);
        await session.World.BroadcastCreatureHealthAsync(creature, ct);
        if (died)
        {
            await killReward.OnCreatureKilledAsync(session, creature, ct);
            logger.LogDebug("POISON '{User}': яд {Spell} добил '{Name}'", session.Account, poison.SpellId, creature.Template.Name);
        }
        return died;
    }

    /// <summary>
    /// Deadly Poison: накладывает/освежает стек-DoT на цели. Новый заряд (до 5) увеличивает урон тика
    /// (базовый × заряды) и обновляет длительность; визуал — одна аура-иконка с числом зарядов. Тики урона
    /// идёт общий <see cref="PeriodicsService"/> (DoesTick), поэтому ведём эффект в session.Progression.Periodics.
    /// </summary>
    private async Task ApplyDeadlyStackAsync(WorldSession session, WorldCreature creature, uint spellId,
        byte level, long now, CancellationToken ct)
    {
        var perStack = Math.Max(1, (int)level); // базовый урон тика на 1 заряд (без SP/AP — по уровню)
        var existing = session.Progression.Periodics.FirstOrDefault(
            p => p.SpellId == spellId && p.TargetGuid == creature.Guid && p.DoesTick);

        byte stacks;
        byte slot;
        if (existing is not null)
        {
            stacks = (byte)Math.Min(DeadlyMaxStacks, existing.StackCount + 1);
            existing.StackCount = stacks;
            existing.Amount = perStack * stacks;
            existing.ExpiresAtMs = now + DeadlyDurationMs;
            existing.NextTickMs = Math.Min(existing.NextTickMs, now + DeadlyTickIntervalMs);
            slot = existing.Slot;
        }
        else
        {
            stacks = 1;
            slot = (byte)session.Progression.Periodics.Count(p => p.TargetGuid == creature.Guid);
            session.Progression.Periodics.Add(new PeriodicEffect
            {
                SpellId = spellId,
                TargetGuid = creature.Guid,
                SchoolMask = SchoolNature,
                Amount = perStack,
                StackCount = 1,
                IntervalMs = DeadlyTickIntervalMs,
                NextTickMs = now + DeadlyTickIntervalMs,
                ExpiresAtMs = now + DeadlyDurationMs,
                IsHeal = false,
                OwnsVisual = true,
                Slot = slot,
            });
        }

        const byte Flags = AuraFlags.Effect1 | AuraFlags.Negative | AuraFlags.Duration;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAuraUpdate,
            AuraPackets.BuildApplyByCaster(creature.Guid, (ulong)session.InWorldGuid, slot, spellId,
                Flags, level, stacks, DeadlyDurationMs), ct);
        session.Combat.LastCombatMs = now;
        logger.LogDebug("POISON '{User}': Deadly стек {Stacks}/{Max} на '{Name}'",
            session.Account, stacks, DeadlyMaxStacks, creature.Template.Name);
    }

    /// <summary>
    /// Wound Poison: накладывает/освежает стек-дебафф снижения лечения цели (−10% за заряд, до 5 = −50%).
    /// Непериодический (урона нет) — учитывается при лечении цели (<see cref="SpellEffectsService"/>).
    /// </summary>
    private async Task ApplyWoundStackAsync(WorldSession session, WorldCreature creature, uint spellId, long now, CancellationToken ct)
    {
        var existing = session.Progression.Periodics.FirstOrDefault(
            p => p.SpellId == spellId && p.TargetGuid == creature.Guid && p.HealReductionPct != 0);

        byte stacks;
        byte slot;
        if (existing is not null)
        {
            stacks = (byte)Math.Min(WoundMaxStacks, existing.StackCount + 1);
            existing.StackCount = stacks;
            existing.HealReductionPct = stacks * WoundReductionPerStack;
            existing.ExpiresAtMs = now + WoundDurationMs;
            slot = existing.Slot;
        }
        else
        {
            stacks = 1;
            slot = (byte)session.Progression.Periodics.Count(p => p.TargetGuid == creature.Guid);
            session.Progression.Periodics.Add(new PeriodicEffect
            {
                SpellId = spellId,
                TargetGuid = creature.Guid,
                StackCount = 1,
                HealReductionPct = WoundReductionPerStack,
                ExpiresAtMs = now + WoundDurationMs,
                DoesTick = false,
                OwnsVisual = true,
                Slot = slot,
            });
        }

        var level = (byte)(session.Character?.Level ?? 1);
        const byte Flags = AuraFlags.Effect1 | AuraFlags.Negative | AuraFlags.Duration;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAuraUpdate,
            AuraPackets.BuildApplyByCaster(creature.Guid, (ulong)session.InWorldGuid, slot, spellId,
                Flags, level, stacks, WoundDurationMs), ct);
        session.Combat.LastCombatMs = now;
        logger.LogDebug("POISON '{User}': Wound стек {Stacks}/{Max} (−{Pct}% лечения) на '{Name}'",
            session.Account, stacks, WoundMaxStacks, stacks * WoundReductionPerStack, creature.Template.Name);
    }
}
