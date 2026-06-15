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
    CrowdControlService crowdControl, ComboPointService comboPoints, DispelService dispel, ProcService procs,
    RuneService runes, AuraService auras, CreatureCombatAI creatureAi)
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
        // MELEE.1 «на следующий замах»: НЕ шлём SPELL_GO на касте — иначе клиент считает удар совершённым и
        // «отжимает» кнопку. Для on-next-swing SPELL_GO уйдёт на самом замахе (PlayerMeleeService), а до тех
        // пор кнопка остаётся подсвеченной (абилка «в очереди»). Ресурс (ярость) списывается ниже, как на касте.
        if (!info.OnNextSwing)
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

        // RUNE.3: рунная абилка DK — потратить руны нужных типов (ставит их на кулдаун) и начислить силу рун
        // (RP×10, как ярость). Стоимость по spellId (SpellRuneCost). Доступность проверена гейтом каста.
        if (info.PowerType == SpellCastService.PowerRune && RuneService.GetCost(info) is { } runeCost)
        {
            var rpGain = await runes.SpendAsync(session, runeCost, ct);
            if (rpGain > 0)
                await combatResources.GainPowerAsync(session, 6, (uint)(rpGain * 10), ct);
        }

        // RUNE.5: Blood Tap — конвертирует руну крови в death-руну и активирует её.
        if (spellId == RuneService.BloodTapSpellId)
            await runes.BloodTapAsync(session, ct);

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
        // MELEE.1: «на следующий замах» (Героический удар/Раскол/Свирепый удар) — НЕ бьём сейчас, ставим в
        // очередь; следующая автоатака заместится этой абилкой (PlayerMeleeService). Ярость уже списана на касте.
        if (info.OnNextSwing)
        {
            session.Combat.PendingNextSwingSpellId = spellId;
            session.Combat.PendingNextSwingCastCount = castCount; // для SPELL_GO на замахе (снятие подсветки кнопки)
        }
        else if (info.IsHeal)
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

        // IMMUNITY.2: Forbearance — пузыри/Lay on Hands/Avenging Wrath вешают на кастера дебафф 25771 (2 мин),
        // блокирующий их повторное применение (общий КД; гейт — в SpellCastService).
        if (SpellCatalog.IsForbearanceSpell(spellId))
            await auras.ApplyAsync(session, SpellCatalog.ForbearanceDebuffId, SpellCatalog.ForbearanceDurationMs,
                positive: false, form: 0, ct);

        // §8 Оружейный имбу шамана (Flametongue/Frostbrand/Windfury) — энчант (эффект 54), движок его не
        // материализует. Вешаем видимый бафф (эксклюзив — один активный имбу); on-hit прок — ImbueService на свинге.
        if (ImbueService.IsImbue(spellId))
            await auras.ApplyAsync(session, spellId, ImbueDurationMs, positive: true, form: 0, ct,
                group: SpellCatalog.GroupShamanImbue);

        // §8 Яд разбойника (нанесён на оружие через CMSG_USE_ITEM → спелл-применение, эффект 54): видимый
        // бафф (эксклюзив — один активный яд); on-hit прок природного урона — PoisonService на свинге.
        if (PoisonService.IsPoison(spellId))
            await auras.ApplyAsync(session, spellId, ImbueDurationMs, positive: true, form: 0, ct,
                group: SpellCatalog.GroupRoguePoison);

        // Фаза 2 CC: контроль (стан/рут/страх/немота/дезориентация). §4: по площади (Frost Nova/Psychic Scream)
        // — на всех враждебных рядом; иначе — на одну цель-существо.
        if (info.CrowdControl != SpellCatalog.CrowdControlKind.None)
        {
            if (info.IsAreaCrowdControl)
                await crowdControl.ApplyAreaAsync(session, spellId, info, now, ct, durationOverrideMs: finisherDur);
            else if (targetGuid != 0 && session.World.FindCreature(targetGuid) is { } ccTarget)
                await crowdControl.ApplyAsync(session, ccTarget, spellId, info, now, ct, durationOverrideMs: finisherDur);
        }

        // Фаза 2 INT.1: прерывание каста цели-существа (Kick/Counterspell/Pummel) + лок школы.
        if (info.IsInterrupt && targetGuid != 0 && session.World.FindCreature(targetGuid) is { CastingSpellId: not 0 } caster)
            await InterruptCreatureCastAsync(session, caster, info, now, ct);

        // §2 Drain Soul (ЧК): метим цель-существо — при её убийстве игрок получит осколок души (KillRewardService).
        if (SpellCatalog.IsDrainSoul(spellId) && targetGuid != 0 && session.World.FindCreature(targetGuid) is not null)
            session.Combat.DrainSoulTargetGuid = targetGuid;

        // §9 Death Grip (DK): притягиваем цель-существо к ногам игрока (рывок) и вводим в бой (таунт-эффект).
        if (SpellCatalog.IsDeathGrip(spellId) && targetGuid != 0 && session.Player is { } gripPlayer
            && session.World.FindCreature(targetGuid) is { IsAlive: true } gripped)
            await DeathGripAsync(session, gripped, gripPlayer, now, ct);

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

        // PROC.1/PROC.2: прок на вредный спелл фактически шлётся из SpellEffectsService.ApplyDamageAsync
        // (там известны крит и школа для крит-проков). Чистый DoT без прямого урона: шлём событие здесь (без крита).
        if (info.MaxAmount == 0 && info.Periodic && !info.PeriodicHeal)
            await procs.TryProcAsync(session, ProcService.ProcFlagDealHarmfulSpell, ct, wasCrit: false, spellSchoolMask: info.School);

        // §2/M11.3: расход реагентов — для ЛЮБОГО спелла с реагентами (осколок души 6265 у призывов/Soulstone/
        // Healthstone/Soul Fire; травы/порошки крафта и буффов). Гейт «есть ли реагент» — на старте каста.
        if (info.Reagents is not null)
            await crafting.ConsumeReagentsAsync(session, info, ct);
        // M11.3: крафт профессии — создание предмета (реагенты уже списаны выше) + прокачка навыка.
        if (info.CreateItemId != 0)
            await crafting.DoCraftAsync(session, spellId, info, ct);

        // M7 #33: движущий эффект — рывок к цели (Charge) или телепорт (Blink/Shadowstep).
        await ApplyMovementAsync(session, info.Movement, targetGuid, ct);
        // Цепочка триггера: у триггер-спелла резолвим движение (Shadowstep 36554 → 36563 телепорт) И контроль
        // (§9 Intercept 20252 → стан 20253: рывок воина к цели в бою + 3с стан). Эффект самой абилки —
        // рывок (Charge) выше; стан несёт триггер-спелл (EffectTriggerSpell), накладываем после рывка.
        if (info.TriggerSpellId != 0)
        {
            var trig = await spellCatalog.GetAsync(info.TriggerSpellId, ct);
            if (trig is not null)
            {
                await ApplyMovementAsync(session, trig.Movement, targetGuid, ct);
                if (targetGuid != 0 && session.World.FindCreature(targetGuid) is { } trigTarget)
                {
                    // §9 Intercept (20253 — стан): триггер с CC накладываем на цель после рывка.
                    if (trig.CrowdControl != SpellCatalog.CrowdControlKind.None)
                        await crowdControl.ApplyAsync(session, trigTarget, info.TriggerSpellId, trig, now, ct);
                    // §9 Feral Charge — Bear (19675 — эффект 68 INTERRUPT_CAST): триггер прерывает каст цели.
                    else if (trig.IsInterrupt && trigTarget.CastingSpellId != 0)
                        await InterruptCreatureCastAsync(session, trigTarget, trig, now, ct);
                }
            }
        }
    }

    /// <summary>SpellCastResult INTERRUPTED (0x28) — гасит каст-бар существа у клиента. INT.1.</summary>
    private const byte SpellFailedInterrupted = 0x28;

    /// <summary>§8 Длительность оружейного имбу шамана (бафф-визуал): 30 мин, как на оффе.</summary>
    private const int ImbueDurationMs = 30 * 60 * 1000;

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

    /// <summary>§9 Death Grip: быстрый рывок цели-существа к ногам игрока (~2 ярда от него) + ввод в бой (таунт-эффект:
    /// после рывка существо идёт на игрока). Скриптовый эффект (CMaNGOS Spell::EffectDummy → MoveTo). Todo: точная угроза.</summary>
    private async Task DeathGripAsync(WorldSession session, WorldCreature creature, WorldPlayer player, long now, CancellationToken ct)
    {
        var dx = creature.X - player.X;
        var dy = creature.Y - player.Y;
        var dz = creature.Z - player.Z;
        var dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        var flat = MathF.Sqrt(dx * dx + dy * dy);
        float lx = player.X, ly = player.Y;
        if (flat > 0.1f) { var f = 2f / flat; lx = player.X + dx * f; ly = player.Y + dy * f; } // ~2 ярда от игрока
        var durationMs = (uint)Math.Clamp(dist / 30f * 1000f, 100f, 800f); // быстрый рывок ~30 ярд/с
        await session.World.MoveCreatureAsync(creature, lx, ly, player.Z, durationMs, ct);
        await creatureAi.EnsureCreatureRetaliationAsync(session, creature, roar: false, ct);
    }
}
