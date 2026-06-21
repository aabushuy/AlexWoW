using AlexWoW.DataStores;
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
    public bool IsEnergize; // тик ресурса (Кровавая ярость 29131): Amount → +PowerType ресурс игроку.
    public byte PowerType;  // тип ресурса для IsEnergize: 0=мана, 1=ярость, 3=энергия, 6=сила рун.
    public byte Slot;          // слот ауры на цели (для DoT/дебафф-визуала)
    public bool OwnsVisual;    // true — мы шлём AURA_UPDATE на цель; визуал на себе — в системе аур игрока
    public bool DoesTick = true; // false — непериодический бафф/дебафф (только визуал + истечение). M10.4c
    public int HealthBonus;    // +макс. HP от баффа (MOD_INCREASE_HEALTH) — снять при истечении. M10.4c
    public int BlockBonus;     // +% блока от баффа (MOD_BLOCK_PERCENT, напр. «Блок щитом») — снять при истечении.
    public int DodgeBonus;     // +% уклонения от баффа (MOD_DODGE_PERCENT, Evasion рога) — снять при истечении. DODGE.1
    public int AttackPowerBonus;        // +AP мили от баффа (MOD_ATTACK_POWER, Боевой клич) — снять при истечении.
    public int RangedAttackPowerBonus;  // +AP дальнего боя от баффа (MOD_RANGED_ATTACK_POWER, второй эффект Боевого клича).
    public int StatBonus;               // MOD_STAT (29): величина бонуса к стату.
    public byte StatIndex;              // MOD_STAT (29): индекс стата (0=Str,1=Agi,2=Sta,3=Int,4=Spi).
    public bool AllStats;               // KB#224: MOD_STAT с MiscValue=−1 — бонус ко всем 5 статам (Mark of the Wild).
    public int DamageTakenPct; // % получаемого урона (MOD_DAMAGE_PERCENT_TAKEN, «Глухая оборона»; <0 — снижение).
    public int AbsorbRemaining; // ABS.1: остаток пула absorb-щита (SCHOOL_ABSORB/Mana Shield); 0 — не щит.
    public byte AbsorbSchoolMask; // ABS.1: маска школ, которые щит поглощает (127 — все; 4 — огонь Fire Ward).
    public float ManaShieldMultiplier; // ABS.2: Mana Shield — мана за 1 ед. поглощённого урона (1.5); 0 — обычный щит.
    public byte ImmuneSchoolMask; // IMMUNITY.1: «пузырь» — маска школ, урон которых гасится в ноль (Divine Shield/Ice Block 127); 0 — не иммунитет.
    public bool SelfRoot; // IMMUNITY.1: пузырь обездвиживает игрока (Ice Block) — на снятии шлём UNROOT.
    public byte StackCount = 1; // §8 стек-DoT (Deadly Poison): число зарядов; тик = базовый урон × стек.
    public int HealReductionPct; // §8 Wound Poison: −% входящего лечения цели (стек×10); 0 — не дебафф лечения.
    public bool IsCurse; // §3 дебафф-проклятие ЧК на цели — для правила «один кёрс на цель от кастера».
    public int CurseDamageTakenPct; // §3 Curse of the Elements: +% урона совпадающей школы, который цель получает от кастера.
    public byte CurseSchoolMask; // §3 маска школ для CurseDamageTakenPct (126 — вся магия).

    // SPELL.T1: combat ratings (hit/crit/haste/parry) от баффов и MOD_RATING (189). Каждое поле — флэт-вклад
    // в соответствующий резолвер; суммируется по всем активным аурам (TargetGuid==0). Снимаются истечением.
    public float HitChancePct;            // MOD_HIT_CHANCE (54): +% к попаданию ближним боем.
    public float SpellHitChancePct;       // MOD_SPELL_HIT_CHANCE (55): +% к попаданию заклинаниями.
    public float MeleeCritChancePct;      // MOD_CRIT_PERCENT (52): +% к мили-криту (учитывается в RefreshMeleeAsync).
    public float SpellCritChancePct;      // MOD_SPELL_CRIT_CHANCE (57): +% к спелл-криту (учитывается в RollSpellCrit).
    public float ParryChancePct;          // MOD_PARRY_PERCENT (47): +% к парированию.
    public float MeleeHastePct;           // MOD_MELEE_HASTE (138/217): +% к скорости автоатаки ближним боем.
    public float RangedHastePct;          // MOD_RANGED_HASTE (140): +% к скорости автоатаки дальним боем.
    public float SpellHastePct;           // HASTE_SPELLS (216): +% к скорости каста (CastingTime).
    public float AllHastePct;             // HASTE_ALL (193): +% ко ВСЕМУ (мили + ranged + spell + GCD).
    public float ExpertiseReductionPct;   // MOD_EXPERTISE (210) + CR_EXPERTISE из MOD_RATING — снижение dodge/parry противника, %.
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
    CrowdControlService crowdControl,
    CombatResourcesService combatResources)
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

        if (info.PeriodicEnergize)
        {
            // Тик ресурса на себя (Кровавая ярость 29131: +1 ярости/с 10с). Бафф-иконка — через систему
            // аур; накопление в Combat.Rage/Energy/RunicPower + апдейт полоски делает CombatResourcesService.
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
                IsEnergize = true,
                PowerType = info.PeriodicPower,
            });
            return;
        }

        // DoT на существо.
        var creature = targetCreatureGuid != 0 ? session.World.FindCreature(targetCreatureGuid) : null;
        if (creature is null || !creature.IsAlive)
            return;

        // §3 Проклятие-DoT (Curse of Agony/Doom): один кёрс на цель — снять прежний кёрс кастера.
        if (info.IsCurse)
            await RemoveCursesOnTargetAsync(session, targetCreatureGuid, exceptSpellId: spellId, ct);

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
            IsCurse = info.IsCurse,
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
                damageDonePct: info.DamageDonePct, damageDoneSchool: info.DamageDoneSchoolMask,
                speedPctBonus: info.SpeedPctBonus);
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
            if (info.AttackPowerBonus > 0 || info.RangedAttackPowerBonus > 0)
            {
                // +AP мили/дальний бой (Боевой клич): записываем эффект и обновляем UNIT_FIELD_ATTACK_POWER
                // / UNIT_FIELD_RANGED_ATTACK_POWER. По истечении/cancel — пересчёт в RemoveAsync.
                session.Progression.Periodics.RemoveAll(p => p.SpellId == spellId && p.TargetGuid == 0
                    && (p.AttackPowerBonus != 0 || p.RangedAttackPowerBonus != 0));
                session.Progression.Periodics.Add(new PeriodicEffect
                {
                    SpellId = spellId,
                    TargetGuid = 0,
                    ExpiresAtMs = expires,
                    DoesTick = false,
                    AttackPowerBonus = info.AttackPowerBonus,
                    RangedAttackPowerBonus = info.RangedAttackPowerBonus,
                });
                await SendAttackPowerAsync(session, ct);
            }
            // SPELL.T1: combat ratings (hit/crit/haste/parry) — плоские % из аур 47/52/54/55/57/138/140/193/216/217
            // + распределённые из MOD_RATING (189). Заводим один PeriodicEffect с суммой по типам; снимется
            // по истечении. RefreshMeleeAsync пересчитает крит/парри/SwingTime от итоговой суммы.
            var ratingPcts = info.RatingMask != 0
                ? CombatRatingConversion.Distribute(info.RatingMask, info.RatingValue, level)
                : default;
            var hitPct = info.HitChanceFlat + ratingPcts.MeleeHitPct;
            var spellHitPct = info.SpellHitChanceFlat + ratingPcts.SpellHitPct;
            var meleeCritPct = info.MeleeCritFlat + ratingPcts.MeleeCritPct;
            var spellCritPct = info.SpellCritFlat + ratingPcts.SpellCritPct;
            var parryPct = info.ParryFlat + ratingPcts.ParryPct;
            var meleeHastePct = info.MeleeHasteFlat + ratingPcts.MeleeHastePct;
            var rangedHastePct = info.RangedHasteFlat + ratingPcts.RangedHastePct;
            var spellHastePct = info.SpellHasteFlat + ratingPcts.SpellHastePct;
            var allHastePct = info.AllHasteFlat;
            // Expertise: флэт-units (аура 210) × 0.25% + распределённый из MOD_RATING (CR_EXPERTISE).
            var expertisePct = info.ExpertiseFlat * 0.25f + ratingPcts.ExpertiseReductionPct;
            if (hitPct != 0 || spellHitPct != 0 || meleeCritPct != 0 || spellCritPct != 0 || parryPct != 0
                || meleeHastePct != 0 || rangedHastePct != 0 || spellHastePct != 0 || allHastePct != 0
                || expertisePct != 0)
            {
                // Дедуп: если этот же спелл уже даёт rating-эффект — снять прежнюю запись (refresh ауры).
                session.Progression.Periodics.RemoveAll(p => p.SpellId == spellId && p.TargetGuid == 0
                    && (p.HitChancePct != 0 || p.SpellHitChancePct != 0
                        || p.MeleeCritChancePct != 0 || p.SpellCritChancePct != 0 || p.ParryChancePct != 0
                        || p.MeleeHastePct != 0 || p.RangedHastePct != 0
                        || p.SpellHastePct != 0 || p.AllHastePct != 0
                        || p.ExpertiseReductionPct != 0));
                session.Progression.Periodics.Add(new PeriodicEffect
                {
                    SpellId = spellId,
                    TargetGuid = 0,
                    ExpiresAtMs = expires,
                    DoesTick = false,
                    HitChancePct = hitPct,
                    SpellHitChancePct = spellHitPct,
                    MeleeCritChancePct = meleeCritPct,
                    SpellCritChancePct = spellCritPct,
                    ParryChancePct = parryPct,
                    MeleeHastePct = meleeHastePct,
                    RangedHastePct = rangedHastePct,
                    SpellHastePct = spellHastePct,
                    AllHastePct = allHastePct,
                    ExpertiseReductionPct = expertisePct,
                });
                // Серверные резолверы (PlayerMeleeService / CreatureCombatAI / SpellCastService /
                // SpellEffectsService) читают session.Progression.Periodics на лету и складывают эти %
                // к базовым session.Combat.* — без явного пересчёта UPDATE_OBJECT здесь. UI-сводка
                // (PlayerCritPercentage/PlayerParryPercentage) обновится при ближайшем RefreshMeleeAsync
                // (смена экипировки / левел-ап / повторный логин) — за фиксированные баффы это приемлемо.
            }
            if (info.StatBonus != 0)
            {
                // MOD_STAT (PW:Fortitude/Divine Spirit/Mark of the Wild и т.п.): записываем эффект и обновляем
                // UnitStat0..4 + MaxHealth (Stamina) / MaxMana (Intellect).
                // KB#224: AllStats=true (MiscValue=−1) — бонус ко всем 5 статам сразу. При дедупе таких эффектов
                // снимаем все all-stats записи того же спелла, чтобы не дублировать бонус при повторном касте.
                session.Progression.Periodics.RemoveAll(p => p.SpellId == spellId && p.TargetGuid == 0
                    && p.StatBonus != 0
                    && (info.AllStats ? p.AllStats : !p.AllStats && p.StatIndex == info.StatIndex));
                session.Progression.Periodics.Add(new PeriodicEffect
                {
                    SpellId = spellId,
                    TargetGuid = 0,
                    ExpiresAtMs = expires,
                    DoesTick = false,
                    StatBonus = info.StatBonus,
                    StatIndex = info.StatIndex,
                    AllStats = info.AllStats,
                });
                await SendStatsAsync(session, ct);
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

        // Дебафф на существо (визуал; стат-эффект пока не моделируется, кроме §3 Curse of the Elements).
        var creature = targetCreatureGuid != 0 ? session.World.FindCreature(targetCreatureGuid) : null;
        if (creature is null || !creature.IsAlive)
            return;
        // §3 Проклятие: один кёрс на цель от кастера — новый снимает прежний (включая кёрс-DoT, напр. Agony).
        if (info.IsCurse)
            await RemoveCursesOnTargetAsync(session, targetCreatureGuid, exceptSpellId: spellId, ct);
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
            IsCurse = info.IsCurse,
            CurseDamageTakenPct = info.CurseDamageTakenPct,
            CurseSchoolMask = info.CurseSchoolMask,
            // Mortal Wound (Mortal Strike): −% к лечению цели — переиспользуем поле HealReductionPct
            // (с Wound Poison). Применяется в SpellEffectsService.ApplyHealAsync через HealReductionPctFor.
            HealReductionPct = info.HealingReductionPct,
        });
    }

    /// <summary>§3 Снимает все активные проклятия кастера на цели (кроме exceptSpellId) — правило «один кёрс на цель».</summary>
    private async Task RemoveCursesOnTargetAsync(WorldSession session, ulong targetGuid, uint exceptSpellId, CancellationToken ct)
    {
        foreach (var c in session.Progression.Periodics
                     .Where(p => p.TargetGuid == targetGuid && p.IsCurse && p.SpellId != exceptSpellId).ToList())
            await RemoveAsync(session, c, ct);
    }

    /// <summary>§8 Wound Poison: суммарный % снижения входящего лечения цели (дебаффы кастера на ней; кап 50%).</summary>
    internal static int HealReductionPctFor(WorldSession session, ulong targetGuid)
    {
        if (targetGuid == 0)
            return 0;
        var pct = session.Progression.Periodics
            .Where(p => p.TargetGuid == targetGuid && p.HealReductionPct != 0)
            .Sum(p => p.HealReductionPct);
        return Math.Clamp(pct, 0, 50);
    }

    /// <summary>§3 Множитель урона по проклятой цели: Curse of the Elements (+% урона совпадающей школы от кастера).</summary>
    internal static int CurseAmplify(WorldSession session, ulong targetGuid, byte schoolMask, int damage)
    {
        if (damage <= 0 || targetGuid == 0)
            return damage;
        var pct = session.Progression.Periodics
            .Where(p => p.TargetGuid == targetGuid && p.CurseDamageTakenPct != 0 && (p.CurseSchoolMask & schoolMask) != 0)
            .Sum(p => p.CurseDamageTakenPct);
        return pct != 0 ? damage + damage * pct / 100 : damage;
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
                if (p.IsEnergize)
                    await TickEnergizeAsync(session, p, ct);
                else if (p.IsHeal)
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
        // §3 Curse of the Elements: +% урона совпадающей школы по проклятой цели (амплифицируем и тики DoT).
        amount = (uint)CurseAmplify(session, p.TargetGuid, p.SchoolMask, (int)amount);
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

    /// <summary>Тик energize-ауры: +Amount ресурса PowerType игроку (Кровавая ярость 29131 — +1 ярости/с).
    /// Без лога периодики — клиент сам анимирует прирост ресурса. Мана — фолбэк через прямое поле/пакет
    /// (не реализуем здесь: периодик-мана-ауры в WotLK редки и для воина не актуальны).</summary>
    private async Task TickEnergizeAsync(WorldSession session, PeriodicEffect p, CancellationToken ct)
    {
        if (p.Amount <= 0)
            return;
        await combatResources.GainPowerAsync(session, p.PowerType, (uint)p.Amount, ct);
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

    /// <summary>MOD_STAT (29): применяет сумму активных аур-бонусов статов к UnitStat0..4. Stamina даёт
    /// +10 MaxHealth/единицу, Intellect — +15 MaxMana/единицу (упрощённые формулы CMaNGOS). Без аур-бонуса —
    /// возвращает к базе из <see cref="SessionCombatState.BaseStr"/>… и <see cref="SessionCombatState.BaseMaxHealth"/>.</summary>
    // internal: dev-редактор характеристик (SetStatCommand) пушит первичные статы после session-оверрайда BaseX.
    internal Task SendStatsAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return Task.CompletedTask;
        var bonus = new int[5];
        foreach (var p in session.Progression.Periodics.Where(p => p.TargetGuid == 0 && p.StatBonus != 0))
        {
            if (p.AllStats)
            {
                // KB#224: бонус ко всем 5 статам сразу (Mark of the Wild и т.п.).
                for (var i = 0; i < 5; i++)
                    bonus[i] += p.StatBonus;
            }
            else
            {
                bonus[p.StatIndex] += p.StatBonus;
            }
        }
        var s0 = (uint)Math.Max(0, (int)session.Combat.BaseStr + bonus[0]);
        var s1 = (uint)Math.Max(0, (int)session.Combat.BaseAgi + bonus[1]);
        var s2 = (uint)Math.Max(0, (int)session.Combat.BaseSta + bonus[2]);
        var s3 = (uint)Math.Max(0, (int)session.Combat.BaseInt + bonus[3]);
        var s4 = (uint)Math.Max(0, (int)session.Combat.BaseSpi + bonus[4]);
        // Stamina/Intellect → HP/Mana. Используем простую CMaNGOS-формулу ×10/×15.
        var hpBonus = bonus[2] * 10;
        var manaBonus = bonus[3] * 15;
        var maxHp = (uint)Math.Max(1, (int)session.Combat.BaseMaxHealth + hpBonus);
        var maxMana = (uint)Math.Max(0, (int)session.Cast.BaseMaxMana + manaBonus);
        var oldMaxHp = session.Combat.MaxHealth;
        session.Combat.MaxHealth = maxHp;
        if (hpBonus > 0 && maxHp > oldMaxHp)
            session.Combat.Health += maxHp - oldMaxHp; // прирост HP при наложении баффа выносливости
        else if (hpBonus < 0 && session.Combat.Health > maxHp)
            session.Combat.Health = maxHp;            // если бафф снят и HP больше нового максимума — обрезать
        if (session.Cast.MaxMana > 0)
        {
            var oldMaxMana = session.Cast.MaxMana;
            session.Cast.MaxMana = maxMana;
            if (manaBonus > 0 && maxMana > oldMaxMana)
                session.Cast.Mana += maxMana - oldMaxMana;
            else if (manaBonus < 0 && session.Cast.Mana > maxMana)
                session.Cast.Mana = maxMana;
        }
        return session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.UnitStat0, s0);
                m.SetUInt32(UpdateField.UnitStat1, s1);
                m.SetUInt32(UpdateField.UnitStat2, s2);
                m.SetUInt32(UpdateField.UnitStat3, s3);
                m.SetUInt32(UpdateField.UnitStat4, s4);
                m.SetUInt32(UpdateField.UnitMaxHealth, session.Combat.MaxHealth);
                m.SetUInt32(UpdateField.UnitHealth, session.Combat.Health);
                if (session.Cast.MaxMana > 0)
                {
                    m.SetUInt32(UpdateField.UnitMaxPower1, session.Cast.MaxMana);
                    m.SetUInt32(UpdateField.UnitPower1, session.Cast.Mana);
                }
            }), ct);
    }

    /// <summary>
    /// Dev-редактор: пуш ТОЛЬКО полей статов (UnitStat0..4 = BaseX + аур-бонусы), без пересчёта MaxHealth/Mana.
    /// Полный <see cref="SendStatsAsync"/> завязан на <c>BaseMaxHealth</c>, который при обычном входе может быть
    /// не инициализирован (=0) → MaxHealth схлопывается в 1. Для session-оверрайда статов это не нужно.
    /// </summary>
    internal Task SendStatFieldsAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return Task.CompletedTask;
        var bonus = new int[5];
        foreach (var p in session.Progression.Periodics.Where(p => p.TargetGuid == 0 && p.StatBonus != 0))
        {
            if (p.AllStats) { for (var i = 0; i < 5; i++) bonus[i] += p.StatBonus; }
            else bonus[p.StatIndex] += p.StatBonus;
        }
        var c = session.Combat;
        return session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.UnitStat0, (uint)Math.Max(0, (int)c.BaseStr + bonus[0]));
                m.SetUInt32(UpdateField.UnitStat1, (uint)Math.Max(0, (int)c.BaseAgi + bonus[1]));
                m.SetUInt32(UpdateField.UnitStat2, (uint)Math.Max(0, (int)c.BaseSta + bonus[2]));
                m.SetUInt32(UpdateField.UnitStat3, (uint)Math.Max(0, (int)c.BaseInt + bonus[3]));
                m.SetUInt32(UpdateField.UnitStat4, (uint)Math.Max(0, (int)c.BaseSpi + bonus[4]));
            }), ct);
    }

    /// <summary>+AP/RAP от ауры (Боевой клич): сумма бонусов аур → UNIT_FIELD_ATTACK_POWER /
    /// UNIT_FIELD_RANGED_ATTACK_POWER (UI «Сила атаки»). Кэшируем бонус в <see cref="SessionCombatState"/>,
    /// чтобы PlayerMeleeService использовал актуальное значение в формуле автоатаки без повторного суммирования.</summary>
    // internal: dev-редактор «Характеристики» пушит Силу атаки после session-оверрайда BaseMeleeAttackPower.
    internal Task SendAttackPowerAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return Task.CompletedTask;
        var ap = session.Progression.Periodics.Where(p => p.TargetGuid == 0).Sum(p => p.AttackPowerBonus);
        var rap = session.Progression.Periodics.Where(p => p.TargetGuid == 0).Sum(p => p.RangedAttackPowerBonus);
        session.Combat.AttackPowerBonus = ap;
        session.Combat.RangedAttackPowerBonus = rap;
        var meleeAp = (uint)Math.Max(0, (int)session.Combat.BaseMeleeAttackPower + ap);
        var rangedAp = (uint)Math.Max(0, (int)session.Combat.BaseRangedAttackPower + rap);
        return session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.UnitAttackPower, meleeAp);
                m.SetUInt32(UpdateField.UnitRangedAttackPower, rangedAp);
            }), ct);
    }

    /// <summary>Ф2 #1: пуш combat-rating оверрайдов dev-редактора в PLAYER_FIELD_COMBAT_RATING_1 (лист персонажа
    /// показывает «Рейтинг меткости» и т.п.). Пока — меткость; expertise/защита/устойчивость добавятся сюда же.</summary>
    internal Task SendCombatRatingsAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return Task.CompletedTask;
        return session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.CombatRatingField((int)CombatRatingConversion.CombatRating.HitMelee),
                    session.Combat.BaseMeleeHitRating);
            }), ct);
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

        // Снять +AP/RAP (Боевой клич) — пересчитать без истёкшего эффекта (UI «Сила атаки» вернётся к базе).
        if (p.AttackPowerBonus != 0 || p.RangedAttackPowerBonus != 0)
            await SendAttackPowerAsync(session, ct);

        // Снять +Stat (PW:Fortitude и т.п.) — пересчитать без истёкшего эффекта (статы + MaxHealth/MaxMana).
        if (p.StatBonus != 0)
            await SendStatsAsync(session, ct);

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
