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
    public byte Slot;          // слот ауры на цели (для DoT/дебафф-визуала)
    public bool OwnsVisual;    // true — мы шлём AURA_UPDATE на цель; визуал на себе — в системе аур игрока
    public bool DoesTick = true; // false — непериодический бафф/дебафф (только визуал + истечение). M10.4c
    public int HealthBonus;    // +макс. HP от баффа (MOD_INCREASE_HEALTH) — снять при истечении. M10.4c
    public int BlockBonus;     // +% блока от баффа (MOD_BLOCK_PERCENT, напр. «Блок щитом») — снять при истечении.
    public int DodgeBonus;     // +% уклонения от баффа (MOD_DODGE_PERCENT, Evasion рога) — снять при истечении. DODGE.1
    public int DamageTakenPct; // % получаемого урона (MOD_DAMAGE_PERCENT_TAKEN, «Глухая оборона»; <0 — снижение).
    public int AbsorbRemaining; // ABS.1: остаток пула absorb-щита (SCHOOL_ABSORB/Mana Shield); 0 — не щит.
    public byte AbsorbSchoolMask; // ABS.1: маска школ, которые щит поглощает (127 — все; 4 — огонь Fire Ward).
    public float ManaShieldMultiplier; // ABS.2: Mana Shield — мана за 1 ед. поглощённого урона (1.5); 0 — обычный щит.
    public byte ImmuneSchoolMask; // IMMUNITY.1: «пузырь» — маска школ, урон которых гасится в ноль (Divine Shield/Ice Block 127); 0 — не иммунитет.
    public bool SelfRoot; // IMMUNITY.1: пузырь обездвиживает игрока (Ice Block) — на снятии шлём UNROOT.
}

/// <summary>
/// Периодические ауры (M10.4b, DI-сервис M7 S3 — бывший статик Periodics): DoT (урон по существу во времени)
/// и HoT (хил себе). Тик в серверном цикле (<see cref="World.WorldTick.UpdateAsync"/>). DoT кладёт дебафф на
/// существо (SMSG_AURA_UPDATE с реальным кастером) и тикает урон (SMSG_PERIODICAURALOG); HoT использует
/// бафф-иконку системы аур (M6.11) + тикает хил. Величина/интервал/длительность — из spell_template
/// (BasePoints+1, EffectAmplitude, SpellDuration.dbc).
/// </summary>
internal sealed class PeriodicsService(
    AuraService auras,
    SpellCatalog spellCatalog,
    CreatureCombatAI creatureAi,
    SpellTestCaptureService spellTestCapture,
    KillRewardService killReward,
    CrowdControlService crowdControl)
{
    /// <summary>Накладывает периодический эффект каста (после применения прямого эффекта). M10.4b.
    /// <paramref name="durationOverrideMs"/>&gt;0 — взять вместо полной длительности (восстановление с остатком, M10.5).</summary>
    internal async Task ApplyAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetCreatureGuid, CancellationToken ct, int durationOverrideMs = 0, byte comboPoints = 0)
    {
        if (!info.Periodic || info.AuraDurationMs <= 0 || session.InWorldGuid == 0)
            return;

        // M10.6: модификаторы талантов — величина тика (ALL_EFFECTS/EFFECT{N} + SPELLMOD_DOT, напр.
        // Improved Rend) и длительность (SPELLMOD_DURATION). Остаток при восстановлении не трогаем.
        var mods = session.Progression.SpellMods;
        // CP.3: DoT-финишер (Rupture) — бонус к тику за каждое израсходованное очко серии (до модификаторов).
        var baseTick = info.TickAmount + (comboPoints > 0 ? comboPoints * info.ComboTickPerPoint : 0);
        var tickAmount = SpellModifiers.Apply(mods, info, SpellModOp.Dot,
            SpellModifiers.ApplyEffectValue(mods, info, info.PeriodicEffectIndex, baseTick));
        var dur = durationOverrideMs > 0
            ? durationOverrideMs
            : Math.Max(0, SpellModifiers.Apply(mods, info, SpellModOp.Duration, info.AuraDurationMs));
        var interval = info.TickIntervalMs > 0 ? info.TickIntervalMs : 3000;
        var now = Environment.TickCount64;
        var expires = now + dur;
        var caster = (ulong)session.InWorldGuid;
        var level = (byte)(session.Character?.Level ?? 1);

        if (info.PeriodicHeal)
        {
            // HoT на себя: бафф-иконка — через систему аур (M6.11), тик хила — здесь.
            session.Progression.Periodics.RemoveAll(p => p.SpellId == spellId && p.TargetGuid == 0);
            await auras.ApplyAsync(session, spellId, dur, positive: true, form: 0, ct);
            session.Progression.Periodics.Add(new PeriodicEffect
            {
                SpellId = spellId,
                TargetGuid = 0,
                SchoolMask = info.School,
                Amount = tickAmount,
                IntervalMs = interval,
                NextTickMs = now + interval,
                ExpiresAtMs = expires,
                IsHeal = true,
            });
            return;
        }

        // DoT на существо.
        var creature = targetCreatureGuid != 0 ? session.World.FindCreature(targetCreatureGuid) : null;
        if (creature is null || !creature.IsAlive)
            return;

        // Рефреш: снять прежний экземпляр того же DoT на этой цели (тот же слот).
        var dup = session.Progression.Periodics.FirstOrDefault(p => p.SpellId == spellId && p.TargetGuid == targetCreatureGuid);
        byte slot;
        if (dup is not null) { slot = dup.Slot; session.Progression.Periodics.Remove(dup); }
        else
        {
            slot = (byte)session.Progression.Periodics.Count(p => p.TargetGuid == targetCreatureGuid);
        }

        const byte Flags = AuraFlags.Effect1 | AuraFlags.Negative | AuraFlags.Duration;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAuraUpdate,
            AuraPackets.BuildApplyByCaster(creature.Guid, caster, slot, spellId, Flags, level, 1, dur), ct);
        session.Progression.Periodics.Add(new PeriodicEffect
        {
            SpellId = spellId,
            TargetGuid = targetCreatureGuid,
            SchoolMask = info.School,
            Amount = tickAmount,
            IntervalMs = interval,
            NextTickMs = now + interval,
            ExpiresAtMs = expires,
            IsHeal = false,
            OwnsVisual = true,
            Slot = slot,
        });
    }

    /// <summary>
    /// Накладывает непериодический бафф/дебафф (M10.4c): по знаку BasePoints — бафф на себя (иконка через
    /// систему аур M6.11) либо дебафф на цель-существо (AURA_UPDATE с кастером). Механика — только простой
    /// +макс.HP (MOD_INCREASE_HEALTH); прочие стат-моды визуальны (боевая модель упрощена).
    /// </summary>
    internal async Task ApplyAuraEffectAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetCreatureGuid, CancellationToken ct, int durationOverrideMs = 0)
    {
        if (!info.AuraBuff || info.AuraDurationMs <= 0 || session.InWorldGuid == 0)
            return;
        // M10.6: SPELLMOD_DURATION талантов (напр. Booming Voice удлиняет Боевой клич).
        var dur = durationOverrideMs > 0
            ? durationOverrideMs
            : Math.Max(0, SpellModifiers.Apply(session.Progression.SpellMods, info, SpellModOp.Duration, info.AuraDurationMs));
        var now = Environment.TickCount64;
        var expires = now + dur;
        var caster = (ulong)session.InWorldGuid;
        var level = (byte)(session.Character?.Level ?? 1);

        if (info.AuraPositive)
        {
            // Бафф на себя: иконка — через систему аур; простой эффект (+макс.HP / +% блока) — здесь, со снятием по истечении.
            // Эксклюзивная группа (Фаза 2): брони мага/чернокнижника взаимоисключающие — новая снимает прежнюю.
            // §1: форма (info.ShapeshiftForm) передаётся в AuraService → байт формы UNIT_FIELD_BYTES_2 + модель
            // (Metamorphosis ЧК: форма 22 → демон-модель). По истечении баффа AuraService снимет форму (resetForm).
            var exclusiveGroup = SpellCatalog.ExclusiveAuraGroup(spellId);
            await auras.ApplyAsync(session, spellId, dur, positive: true, form: info.ShapeshiftForm, ct, group: exclusiveGroup,
                damageDonePct: info.DamageDonePct, damageDoneSchool: info.DamageDoneSchoolMask);
            if (info.HealthBonus > 0)
            {
                session.Progression.Periodics.RemoveAll(p => p.SpellId == spellId && p.TargetGuid == 0);
                session.Combat.MaxHealth += (uint)info.HealthBonus;
                session.Combat.Health += (uint)info.HealthBonus;
                if (session.Player is { } pl)
                    await session.World.BroadcastPlayerHealthAsync(pl, ct);
                session.Progression.Periodics.Add(new PeriodicEffect
                {
                    SpellId = spellId,
                    TargetGuid = 0,
                    ExpiresAtMs = expires,
                    DoesTick = false,
                    HealthBonus = info.HealthBonus,
                });
            }
            if (info.BlockBonus != 0)
            {
                // +% блока («Блок щитом»): записываем эффект и пересчитываем PLAYER_BLOCK_PERCENTAGE.
                session.Progression.Periodics.RemoveAll(p => p.SpellId == spellId && p.TargetGuid == 0 && p.BlockBonus != 0);
                session.Progression.Periodics.Add(new PeriodicEffect
                {
                    SpellId = spellId,
                    TargetGuid = 0,
                    ExpiresAtMs = expires,
                    DoesTick = false,
                    BlockBonus = info.BlockBonus,
                });
                await SendBlockAsync(session, ct);
            }
            if (info.DodgePct != 0)
            {
                // DODGE.1: +% уклонения (Evasion) — записываем эффект и обновляем PLAYER_DODGE_PERCENTAGE.
                session.Progression.Periodics.RemoveAll(p => p.SpellId == spellId && p.TargetGuid == 0 && p.DodgeBonus != 0);
                session.Progression.Periodics.Add(new PeriodicEffect
                {
                    SpellId = spellId,
                    TargetGuid = 0,
                    ExpiresAtMs = expires,
                    DoesTick = false,
                    DodgeBonus = info.DodgePct,
                });
                await SendDodgeAsync(session, ct);
            }
            if (info.DamageTakenPct != 0)
            {
                // Снижение получаемого урона («Глухая оборона») — учитывается в обработке входящего удара.
                session.Progression.Periodics.RemoveAll(p => p.SpellId == spellId && p.TargetGuid == 0 && p.DamageTakenPct != 0);
                session.Progression.Periodics.Add(new PeriodicEffect
                {
                    SpellId = spellId,
                    TargetGuid = 0,
                    ExpiresAtMs = expires,
                    DoesTick = false,
                    DamageTakenPct = info.DamageTakenPct,
                });
            }
            if (info.AbsorbAmount > 0)
            {
                // ABS.1: absorb-щит — пул поглощения на эффекте; гасит входящий урон по своей школе до исчерпания.
                session.Progression.Periodics.RemoveAll(p => p.SpellId == spellId && p.TargetGuid == 0 && p.AbsorbRemaining != 0);
                session.Progression.Periodics.Add(new PeriodicEffect
                {
                    SpellId = spellId,
                    TargetGuid = 0,
                    ExpiresAtMs = expires,
                    DoesTick = false,
                    AbsorbRemaining = info.AbsorbAmount,
                    AbsorbSchoolMask = info.AbsorbSchoolMask,
                    ManaShieldMultiplier = info.ManaShieldMultiplier,
                });
            }
            if (info.ImmuneSchoolMask != 0)
            {
                // IMMUNITY.1: «пузырь» неуязвимости (Divine Shield/Ice Block/Hand of Protection) — флаг на эффекте;
                // пока активен, входящий урон совпадающей школы гасится в ноль (см. CreatureCombatAI).
                session.Progression.Periodics.RemoveAll(p => p.SpellId == spellId && p.TargetGuid == 0 && p.ImmuneSchoolMask != 0);
                session.Progression.Periodics.Add(new PeriodicEffect
                {
                    SpellId = spellId,
                    TargetGuid = 0,
                    ExpiresAtMs = expires,
                    DoesTick = false,
                    ImmuneSchoolMask = info.ImmuneSchoolMask,
                    SelfRoot = info.ImmuneSelfRoot,
                });
                if (info.ImmuneSelfRoot)
                    // Ice Block «вмёрз в глыбу» — обездвиживаем игрока (UNROOT на снятии в RemoveAsync).
                    await session.SendAsync(WorldOpcode.SmsgForceMoveRoot,
                        MovementPackets.BuildForceMoveRoot(caster, session.NextTeleportCounter()), ct);
            }
            return;
        }

        // Дебафф на существо (визуал; стат-эффект пока не моделируется).
        var creature = targetCreatureGuid != 0 ? session.World.FindCreature(targetCreatureGuid) : null;
        if (creature is null || !creature.IsAlive)
            return;
        var dup = session.Progression.Periodics.FirstOrDefault(p => p.SpellId == spellId && p.TargetGuid == targetCreatureGuid);
        byte slot;
        if (dup is not null) { slot = dup.Slot; session.Progression.Periodics.Remove(dup); }
        else
        {
            slot = (byte)session.Progression.Periodics.Count(p => p.TargetGuid == targetCreatureGuid);
        }

        const byte Flags = AuraFlags.Effect1 | AuraFlags.Negative | AuraFlags.Duration;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAuraUpdate,
            AuraPackets.BuildApplyByCaster(creature.Guid, caster, slot, spellId, Flags, level, 1, dur), ct);
        session.Progression.Periodics.Add(new PeriodicEffect
        {
            SpellId = spellId,
            TargetGuid = targetCreatureGuid,
            ExpiresAtMs = expires,
            DoesTick = false,
            OwnsVisual = true,
            Slot = slot,
        });
    }

    /// <summary>
    /// Восстанавливает временну́ю свою ауру при входе (M10.5) с остатком длительности: по данным spell_template
    /// решает — HoT (тик хила), бафф с +макс.HP, или просто бафф-иконка. Дебаффы/DoT на врагах не персистятся.
    /// </summary>
    internal async Task RestoreTimedAuraAsync(WorldSession session, uint spellId, int remainingMs, CancellationToken ct)
    {
        if (remainingMs <= 0)
            return;
        SpellCatalog.SpellInfo? info;
        try { info = await spellCatalog.GetAsync(spellId, ct); }
        catch { info = null; }

        if (info is { Periodic: true, PeriodicHeal: true })
            await ApplyAsync(session, spellId, info, targetCreatureGuid: 0, ct, durationOverrideMs: remainingMs);
        else if (info is { AuraBuff: true, AuraPositive: true })
            await ApplyAuraEffectAsync(session, spellId, info, targetCreatureGuid: 0, ct, durationOverrideMs: remainingMs);
        else
            // Прочий временны́й бафф (напр. через .buff) — только иконка с остатком длительности.
            await auras.ApplyAsync(session, spellId, remainingMs, positive: true, form: 0, ct);
    }

    /// <summary>Тик периодических эффектов (из WorldTick.UpdateAsync): применяет урон/хил, снимает истёкшие.</summary>
    internal async Task TickAsync(WorldSession session, long now, CancellationToken ct)
    {
        if (session.Progression.Periodics.Count == 0 || session.InWorldGuid == 0)
            return;
        var caster = (ulong)session.InWorldGuid;

        foreach (var p in session.Progression.Periodics.ToList())
        {
            if (p.DoesTick && p.NextTickMs <= now && now < p.ExpiresAtMs + p.IntervalMs)
            {
                p.NextTickMs += p.IntervalMs;
                if (p.IsHeal)
                    await TickHealAsync(session, p, caster, ct);
                else
                    await TickDamageAsync(session, p, caster, now, ct);
            }
            if (now >= p.ExpiresAtMs && session.Progression.Periodics.Contains(p))
                await RemoveAsync(session, p, ct);
        }
    }

    private async Task TickDamageAsync(WorldSession session, PeriodicEffect p, ulong caster, long now, CancellationToken ct)
    {
        var creature = session.World.FindCreature(p.TargetGuid);
        if (creature is null || !creature.IsAlive)
        {
            await RemoveAsync(session, p, ct);
            return;
        }
        session.Combat.LastCombatMs = now;
        // Фаза 2: % наносимого урона по школе (Shadowform +15% Shadow к DoT — SW:Pain/Mind Flay и т.п.).
        var amount = (uint)Math.Max(1, DamageDoneModifier.Apply(session, p.SchoolMask, p.Amount));
        var (_, _, died) = session.World.ApplyCreatureDamage(creature, amount);
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgPeriodicAuraLog,
            AuraPackets.BuildPeriodicLog(creature.Guid, caster, p.SpellId, isHeal: false, amount, p.SchoolMask), ct);
        await session.World.BroadcastCreatureHealthAsync(creature, ct);
        // M12 Spell QA: захват тика DoT (ручной режим; харнесс пишет синтетический тик).
        await spellTestCapture.RecordTickAsync(session, p.SpellId, p.SchoolMask, isHeal: false, amount, (uint)Math.Max(0, p.Amount), ct);
        if (died)
        {
            await killReward.OnCreatureKilledAsync(session, creature, ct);
            await RemoveAsync(session, p, ct);
        }
        else
        {
            // §4 break-on-damage: тик DoT тоже ломает Polymorph/Disorient/Fear на цели.
            await crowdControl.TryBreakOnDamageAsync(session.World, creature, now, ct);
            await creatureAi.EnsureCreatureRetaliationAsync(session, creature, roar: false, ct);
        }
    }

    private async Task TickHealAsync(WorldSession session, PeriodicEffect p, ulong caster, CancellationToken ct)
    {
        if (session.Player is not { } player)
            return;
        var before = session.Combat.Health;
        session.Combat.Health = Math.Min(session.Combat.MaxHealth, before + (uint)Math.Max(1, p.Amount));
        var effective = session.Combat.Health - before;
        await session.World.BroadcastToPlayerObserversAsync(player, WorldOpcode.SmsgPeriodicAuraLog,
            AuraPackets.BuildPeriodicLog(player.Guid, caster, p.SpellId, isHeal: true, effective, 0), ct);
        await session.World.BroadcastPlayerHealthAsync(player, ct);
        // M12 Spell QA: захват тика HoT (ручной режим; харнесс пишет синтетический тик).
        await spellTestCapture.RecordTickAsync(session, p.SpellId, p.SchoolMask, isHeal: true, effective, (uint)Math.Max(0, p.Amount), ct);
    }

    /// <summary>Снимает свой периодический эффект/бафф по spellId (правый клик по иконке, M10.4c) — откат +макс.HP.</summary>
    internal async Task CancelSelfAsync(WorldSession session, uint spellId, CancellationToken ct)
    {
        foreach (var p in session.Progression.Periodics.Where(p => p.TargetGuid == 0 && p.SpellId == spellId).ToList())
            await RemoveAsync(session, p, ct);
    }

    /// <summary>Пересчитывает и шлёт PLAYER_BLOCK_PERCENTAGE: база (класс+щит) + сумма активных аур-бонусов блока.</summary>
    private Task SendBlockAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0 || session.Character is not { } c)
            return Task.CompletedTask;
        var bonus = session.Progression.Periodics.Where(p => p.TargetGuid == 0).Sum(p => p.BlockBonus);
        var block = CombatStats.BlockPercent(c.Class, session.Combat.HasShield, bonus);
        session.Combat.BlockPct = block; // синхронизируем кэш — иначе серверный резолвер блока не видит «Блок щитом»
        return session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid,
                m => m.SetFloat(UpdateField.PlayerBlockPercentage, block)), ct);
    }

    /// <summary>DODGE.1: обновляет PLAYER_DODGE_PERCENTAGE = базовый dodge (из статов, <see cref="SessionCombatState.DodgePct"/>)
    /// + сумма активных аур-бонусов уклонения (Evasion). Базовый кэш не трогаем — резолвер удара добавляет бонус сам.</summary>
    private Task SendDodgeAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return Task.CompletedTask;
        var dodge = session.Combat.DodgePct + session.Progression.Periodics.Where(p => p.TargetGuid == 0).Sum(p => p.DodgeBonus);
        return session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid,
                m => m.SetFloat(UpdateField.PlayerDodgePercentage, dodge)), ct);
    }

    private async Task RemoveAsync(WorldSession session, PeriodicEffect p, CancellationToken ct)
    {
        session.Progression.Periodics.Remove(p);

        // Снять простой бафф +макс.HP (M10.4c) — вернуть HP к норме (текущее не выше нового максимума).
        if (p.HealthBonus > 0)
        {
            session.Combat.MaxHealth = session.Combat.MaxHealth > (uint)p.HealthBonus ? session.Combat.MaxHealth - (uint)p.HealthBonus : 1;
            session.Combat.Health = Math.Min(session.Combat.Health, session.Combat.MaxHealth);
            if (session.Player is { } pl)
                await session.World.BroadcastPlayerHealthAsync(pl, ct);
        }

        // Снять +% блока («Блок щитом») — пересчитать блок без истёкшего эффекта.
        if (p.BlockBonus != 0)
            await SendBlockAsync(session, ct);

        // DODGE.1: снять +% уклонения (Evasion) — пересчитать без истёкшего эффекта.
        if (p.DodgeBonus != 0)
            await SendDodgeAsync(session, ct);

        // IMMUNITY.1: снять обездвиживание Ice Block (по истечении/отмене пузыря) — вернуть управление движением.
        if (p.SelfRoot && session.InWorldGuid != 0)
            await session.SendAsync(WorldOpcode.SmsgForceMoveUnroot,
                MovementPackets.BuildForceMoveUnroot((ulong)session.InWorldGuid, session.NextTeleportCounter()), ct);

        if (!p.OwnsVisual)
            return; // визуал на себе (бафф/HoT-иконка) истечёт сам в AuraService.TickAsync (та же длительность)
        var creature = session.World.FindCreature(p.TargetGuid);
        if (creature is not null)
        {
            await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAuraUpdate,
                AuraPackets.BuildRemove(creature.Guid, p.Slot), ct);
        }
    }
}
