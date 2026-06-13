using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Завершение каста (M6.4, выделено из SpellCaster в M7 S3): SPELL_GO + расход ресурса + кулдаун +
/// применение эффектов (<see cref="SpellEffectsService"/>/<see cref="PeriodicsService"/>/<see cref="CraftingService"/>)
/// + движущий эффект и цепочка триггера (M7 #33). Отложенное завершение каста с временем — точно по
/// времени каста (Task.Delay, поколение каста), см. <see cref="ScheduleDeferredCompletion"/>.
/// </summary>
internal sealed class SpellCastCompletion(SpellCatalog spellCatalog, SpellGoSender spellGo,
    ManaRegenService manaRegen, CombatResourcesService combatResources,
    SpellEffectsService spellEffects, PeriodicsService periodics, CraftingService crafting,
    CrowdControlService crowdControl, ComboPointService comboPoints, DispelService dispel, ProcService procs)
{
    /// <summary>
    /// Откладывает завершение каста ТОЧНО на время каста (Task.Delay, не грубый 250-мс тик): завершаем,
    /// только если каст не отменён/не перебит новым (поколение каста). Fire-and-forget осознанно —
    /// исключения логируются внутри, session живёт дольше задачи. M6.4.
    /// </summary>
    internal void ScheduleDeferredCompletion(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetGuid, byte castCount, int gen)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(info.CastMs);
                // Каст не отменён/не перебит новым кастом за это время?
                if (session.Cast.CastGeneration == gen && session.Cast.CastingSpellId == spellId && session.InWorldGuid != 0)
                {
                    session.Cast.CastingSpellId = 0;
                    await CompleteCastAsync(session, spellId, info, targetGuid, castCount, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                session.Logger.LogDebug(ex, "Завершение каста '{User}': {Msg}", session.Account, ex.Message);
            }
        });
    }

    /// <summary>Завершение каста: SPELL_GO + расход маны + кулдаун + применение эффекта (урон/хил) + лог.</summary>
    internal async Task CompleteCastAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetGuid, byte castCount, CancellationToken ct)
    {
        await spellGo.SendSpellGoAsync(session, spellId, targetGuid, castCount, ct);

        // Расход ресурса: мана (правило 5 секунд: реген паузится от LastSpellCastMs) — или ярость/энергия
        // для мили-абилок (списание + апдейт полоски). M10.4a.
        var now = Environment.TickCount64;
        session.Cast.LastSpellCastMs = now;
        var cost = SpellCastService.EffectivePowerCost(session, info);
        if (cost > 0)
        {
            if (info.PowerType == SpellCastService.PowerMana)
            {
                if (session.Cast.MaxMana > 0)
                {
                    session.Cast.Mana = session.Cast.Mana > cost ? session.Cast.Mana - cost : 0;
                    await manaRegen.SendManaUpdateAsync(session, ct);
                }
            }
            else
            {
                await combatResources.SpendPowerAsync(session, info.PowerType, cost, ct);
            }
        }

        // M10.6: начисление ресурса кастеру (ENERGIZE/ярость Рывка) — с модификаторами талантов на
        // величину эффекта (Improved Charge: ALL_EFFECTS +50/+100 к 90 → 14/19 ярости).
        if (info.EnergizeAmount > 0)
        {
            var gain = (uint)Math.Max(0, SpellModifiers.ApplyEffectValue(
                session.Progression.SpellMods, info, info.EnergizeEffectIndex, (int)info.EnergizeAmount));
            if (gain > 0)
            {
                if (info.EnergizePower == SpellCastService.PowerMana)
                {
                    if (session.Cast.MaxMana > 0)
                    {
                        session.Cast.Mana = Math.Min(session.Cast.MaxMana, session.Cast.Mana + gain);
                        await manaRegen.SendManaUpdateAsync(session, ct);
                    }
                }
                else
                {
                    await combatResources.GainPowerAsync(session, info.EnergizePower, gain, ct);
                }
            }
        }

        // Кулдаун: запускаем у клиента (полоска на кнопке) и запоминаем для отказа при раннем рекасте.
        if (info.CooldownMs > 0)
        {
            session.Cast.SpellCooldowns[spellId] = now + info.CooldownMs;
            await session.SendAsync(WorldOpcode.SmsgSpellCooldown,
                SpellPackets.BuildSpellCooldown((ulong)session.InWorldGuid, spellId, (uint)info.CooldownMs), ct);
        }

        // CP.3: финишер расходует очки серии — фиксируем их ДО применения эффекта (скалирование урона/тика).
        var combo = info.IsFinisher ? session.Combat.ComboPoints : (byte)0;
        // CP.3b: длительность финишера с base!=max в SpellDuration.dbc — интерполируется очками (стан/бафф/DoT).
        // 0 — не скалируем (override не используется, берётся базовая длительность эффекта).
        var finisherDur = combo > 0 && info.MaxDurationMs > 0
            ? ComboPointService.ScaledDurationMs(
                info.CrowdControl != SpellCatalog.CrowdControlKind.None ? info.CrowdControlMs : info.AuraDurationMs,
                info.MaxDurationMs, combo)
            : 0;

        // Прямой эффект: хил, либо урон (если есть прямой урон — чистый DoT без прямого числа не шлём).
        if (info.IsHeal)
            await spellEffects.ApplyHealAsync(session, spellId, info, targetGuid, ct);
        else if (info.MaxAmount > 0 || info.WeaponDamage || info.WeaponPercent > 0)
            await spellEffects.ApplyDamageAsync(session, spellId, info, targetGuid, now, ct, combo);

        // M10.4b: периодическая аура (DoT/HoT) — поверх прямого эффекта (напр. Immolate: удар + DoT).
        if (info.Periodic)
            await periodics.ApplyAsync(session, spellId, info, targetGuid, ct, durationOverrideMs: finisherDur, comboPoints: combo);
        // M10.4c: непериодический бафф/дебафф (Battle Shout, Curse of Weakness, Fortitude и т.п.).
        // CC-спеллы (стан/рут/…) сюда НЕ идут — их визуал/состояние ставит CrowdControlService (иначе дубль ауры).
        if (info.AuraBuff && info.CrowdControl == SpellCatalog.CrowdControlKind.None)
            await periodics.ApplyAuraEffectAsync(session, spellId, info, targetGuid, ct, durationOverrideMs: finisherDur);

        // Фаза 2 CC: контроль цели-существа (стан/рут/страх/немота/дезориентация).
        if (info.CrowdControl != SpellCatalog.CrowdControlKind.None && targetGuid != 0
            && session.World.FindCreature(targetGuid) is { } ccTarget)
            await crowdControl.ApplyAsync(session, ccTarget, spellId, info, now, ct, durationOverrideMs: finisherDur);

        // Фаза 2 INT.1: прерывание каста цели-существа (Kick/Counterspell/Pummel) + лок школы.
        if (info.IsInterrupt && targetGuid != 0 && session.World.FindCreature(targetGuid) is { CastingSpellId: not 0 } caster)
            await InterruptCreatureCastAsync(session, caster, info, now, ct);

        // Фаза 2 DSP: диспел. По себе (нет цели / свой guid) — защитный, снимаем свой дебафф (DSP.1).
        // По враждебному существу — атакующий Purge/Spellsteal, снимаем/крадём его бафф (DSP.2).
        if (info.DispelMask != 0)
        {
            if (targetGuid == 0 || targetGuid == (ulong)session.InWorldGuid)
                await dispel.DispelSelfAsync(session, info.DispelMask, ct);
            else if (session.World.FindCreature(targetGuid) is { } dispelTarget)
                await dispel.DispelCreatureAsync(session, dispelTarget, info.DispelMask, info.IsSpellsteal, ct);
        }

        // CP.2: генератор очков серии (Sinister Strike/Backstab/Rake…) — +N очков на цели-существе (кап 5).
        if (info.ComboPointsGenerated > 0 && targetGuid != 0 && session.World.FindCreature(targetGuid) is not null)
            await comboPoints.AddAsync(session, targetGuid, info.ComboPointsGenerated, ct);

        // CP.3: финишер израсходовал все очки серии (эффект уже отмасштабирован зафиксированным `combo`).
        if (info.IsFinisher)
            await comboPoints.ConsumeAsync(session, ct);

        // PROC.1: проки на каст вредного спелла (прямой урон/периодика по цели) — накладывают триггер-бафф.
        if (info.MaxAmount > 0 || (info.Periodic && !info.PeriodicHeal))
            await procs.TryProcAsync(session, ProcService.ProcFlagDealHarmfulSpell, ct);

        // M11.3: крафт профессии — расход реагентов, создание предмета, прокачка навыка.
        if (info.CreateItemId != 0)
            await crafting.DoCraftAsync(session, spellId, info, ct);

        // M7 #33: движущий эффект — рывок к цели (Charge) или телепорт (Blink/Shadowstep).
        await ApplyMovementAsync(session, info.Movement, targetGuid, ct);
        // Цепочка триггера (Shadowstep 36554 → 36563 с эффектом телепорта): резолвим тип у триггера.
        if (info.TriggerSpellId != 0)
        {
            var trig = await spellCatalog.GetAsync(info.TriggerSpellId, ct);
            if (trig is not null)
                await ApplyMovementAsync(session, trig.Movement, targetGuid, ct);
        }
    }

    /// <summary>SpellCastResult INTERRUPTED (0x28) — гасит каст-бар существа у клиента. INT.1.</summary>
    private const byte SpellFailedInterrupted = 0x28;

    /// <summary>
    /// Фаза 2 INT.1: прерывает текущий каст существа и лочит его школу на <see cref="SpellCatalog.SpellInfo.InterruptLockMs"/>.
    /// Шлёт SMSG_SPELL_FAILURE (caster=существо) — клиент гасит каст-бар; AI существа ждёт разлок (CreatureCombatAI).
    /// </summary>
    private async Task InterruptCreatureCastAsync(WorldSession session, WorldCreature caster,
        SpellCatalog.SpellInfo info, long now, CancellationToken ct)
    {
        var interruptedSpell = caster.CastingSpellId;
        // SMSG_SPELL_FAILED_OTHER гасит каст-бар существа у наблюдателей (UNIT_SPELLCAST_INTERRUPTED);
        // SMSG_SPELL_FAILURE — для полноты (как CMaNGOS Spell::SendInterrupted шлёт оба).
        await session.World.BroadcastToObserversAsync(caster, WorldOpcode.SmsgSpellFailedOther,
            SpellPackets.BuildSpellFailedOther(caster.Guid, interruptedSpell, SpellFailedInterrupted), ct);
        await session.World.BroadcastToObserversAsync(caster, WorldOpcode.SmsgSpellFailure,
            SpellPackets.BuildSpellFailure(caster.Guid, interruptedSpell, SpellFailedInterrupted), ct);
        caster.SchoolLockMask = caster.CastSchoolMask;
        caster.SchoolLockUntilMs = now + info.InterruptLockMs;
        caster.CastingSpellId = 0;
        caster.NextCastMs = now + info.InterruptLockMs; // не возобновлять каст до разлока
        session.Logger.LogDebug("INTERRUPT '{User}': прервал каст '{Name}', школа {School} залочена на {Ms}мс",
            session.Account, caster.Template.Name, caster.SchoolLockMask, info.InterruptLockMs);
    }

    /// <summary>Применяет движущий эффект: рывок (сплайн) или телепорт вперёд/за спину цели. M7 #33.</summary>
    private Task ApplyMovementAsync(WorldSession session, SpellCatalog.SpellMovement movement,
        ulong targetGuid, CancellationToken ct) => movement switch
        {
            SpellCatalog.SpellMovement.Charge => spellEffects.ApplyChargeAsync(session, targetGuid, ct),
            SpellCatalog.SpellMovement.TeleportForward => spellEffects.ApplyTeleportAsync(session, targetGuid, behind: false, ct),
            SpellCatalog.SpellMovement.TeleportBehind => spellEffects.ApplyTeleportAsync(session, targetGuid, behind: true, ct),
            _ => Task.CompletedTask,
        };
}
