using System.Collections.Concurrent;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Каталог спеллов (M6.4 → M10.2, DI-синглтон M7 S3): данные эффекта/каста для оркестрации
/// (<see cref="Handlers.SpellCastService"/>).
/// <para>M10.2: основной источник — дамп <c>spell_template</c> (mangos): школа, время каста
/// (<see cref="SpellCastTimes"/>), эффект (урон/хил), мана (флэт или % базовой), кулдаун — читаются из БД
/// и кэшируются (данные спелла иммутабельны). Хардкод снят. Легаси-словарь <see cref="LegacySpells"/>
/// оставлен только фолбэком, если БД мира недоступна.</para>
/// Переключатели (<see cref="Toggles"/>) и стартовый набор (<see cref="GrantedCombatSpells"/>) — наша
/// игровая конфигурация, не из БД (чистые данные — статические члены).
/// </summary>
public sealed class SpellCatalog(IWorldRepository worldDb, ILogger<SpellCatalog> logger)
{
    /// <summary>МАСКИ школ магии (SpellSchoolMask, u8): Fire=0x4, Frost=0x10, Holy=0x2 (см. SMSG_*DAMAGELOG).</summary>
    public const byte SchoolFire = 0x04;
    public const byte SchoolFrost = 0x10;
    public const byte SchoolHoly = 0x02;

    // SpellEffects (Spell.dbc Effect*, сверено с CMaNGOS SpellEffectDefines.h):
    private const int EffectSchoolDamage = 2;            // прямой урон школы
    private const int EffectHeal = 10;                   // прямой хил
    private const int EffectWeaponDamageNoSchool = 17;   // урон оружия (мили-абилки)
    private const int EffectWeaponPercentDamage = 31;    // % урона оружия (BasePoints = процент)
    private const int EffectWeaponDamage = 58;           // урон оружия + бонус
    private const int EffectNormalizedWeaponDmg = 121;   // нормализованный урон оружия + бонус
    private const int EffectApplyAura = 6;               // наложение ауры (периодика и пр.)
    private const int EffectTeleportUnits = 5;           // телепорт юнита (Shadowstep-триггер 36563) — за спину цели
    private const int EffectLeap = 29;                   // прыжок вперёд (Blink) — телепорт по направлению
    private const int EffectTriggerSpell = 64;           // триггер другого спелла (Shadowstep 36554 → 36563)
    private const int EffectCharge = 96;                 // рывок к цели (SPELL_EFFECT_CHARGE) — движение игрока
    private const int EffectCreateItem = 24;             // создание предмета (крафт профессии) — M11.3
    // AuraType (EffectApplyAuraName*, CMaNGOS): периодический урон/хил + простой бонус к HP.
    private const int AuraPeriodicDamage = 3;
    private const int AuraPeriodicHeal = 8;
    private const int AuraModIncreaseHealth = 34;        // +макс. HP (простой эффект баффа, M10.4c)

    /// <summary>
    /// Эффект спелла (M10.2 → M10.4a): школа, диапазон величины (урон/хил/бонус к урону оружия), время каста,
    /// стоимость ресурса (<paramref name="ManaCost"/> флэтом — мана/ярость/энергия по <paramref name="PowerType"/>,
    /// либо <paramref name="ManaCostPct"/> % базовой маны), кулдаун, GCD, хил-ли. <paramref name="WeaponDamage"/> —
    /// мили-абилка: к урону прибавляется бросок оружия; <paramref name="WeaponPercent"/> != 0 — урон = % оружия.
    /// Иммутабельно (зависит только от спелла) → кэшируется по spellId. Урон оружия/ресурс кастера — на касте.
    /// </summary>
    public sealed record SpellInfo(byte School, int MinAmount, int MaxAmount, int CastMs, uint ManaCost,
        int CooldownMs, bool IsHeal = false, uint ManaCostPct = 0, uint GcdMs = 0,
        byte PowerType = 0, bool WeaponDamage = false, uint WeaponPercent = 0,
        bool Periodic = false, bool PeriodicHeal = false, int TickAmount = 0, int TickIntervalMs = 0,
        int AuraDurationMs = 0,
        bool AuraBuff = false, bool AuraPositive = false, int HealthBonus = 0,
        SpellMovement Movement = SpellMovement.None, uint TriggerSpellId = 0,
        uint CreateItemId = 0, uint CreateItemCount = 0,
        IReadOnlyList<(uint Item, uint Count)>? Reagents = null);

    /// <summary>Движущий эффект спелла (M7 #33): рывок к цели (сплайн), телепорт вперёд (Blink) или за спину
    /// цели (Shadowstep). None — не двигает.</summary>
    public enum SpellMovement : byte { None = 0, Charge = 1, TeleportForward = 2, TeleportBehind = 3 }

    /// <summary>Кэш разобранных спеллов (включая «нет в БД» = null), данные иммутабельны. M10.2.</summary>
    private readonly ConcurrentDictionary<uint, SpellInfo?> _cache = new();

    /// <summary>
    /// Эффект спелла: из <c>spell_template</c> (с кэшем); при недоступности БД мира — легаси-фолбэк.
    /// Возвращает null, если спелл не известен ни в БД, ни в фолбэке (клиент сам валидирует — каст без эффекта).
    /// </summary>
    public async Task<SpellInfo?> GetAsync(uint spellId, CancellationToken ct)
    {
        if (_cache.TryGetValue(spellId, out var cached))
            return cached;
        try
        {
            var tpl = await worldDb.GetSpellAsync(spellId, ct);
            var info = tpl is not null ? FromTemplate(tpl) : LegacySpells.GetValueOrDefault(spellId);
            _cache[spellId] = info; // кэшируем определённый результат (в т.ч. null = нет такого спелла)
            return info;
        }
        catch (Exception ex)
        {
            // БД мира недоступна / ошибка маппинга — фолбэк на легаси, без кэша. ЛОГИРУЕМ (раньше глоталось).
            logger.LogError(ex, "SpellCatalog: spell={Spell} — ошибка чтения spell_template, фолбэк на легаси", spellId);
            return LegacySpells.GetValueOrDefault(spellId);
        }
    }

    /// <summary>Маппинг строки spell_template → наша модель эффекта (M10.2).</summary>
    private static SpellInfo FromTemplate(SpellTemplateData t)
    {
        var effects = new[]
        {
            (Eff: t.Effect1, Bp: t.EffectBasePoints1, Ds: t.EffectDieSides1, Aura: t.EffectApplyAuraName1, Amp: t.EffectAmplitude1),
            (Eff: t.Effect2, Bp: t.EffectBasePoints2, Ds: t.EffectDieSides2, Aura: t.EffectApplyAuraName2, Amp: t.EffectAmplitude2),
            (Eff: t.Effect3, Bp: t.EffectBasePoints3, Ds: t.EffectDieSides3, Aura: t.EffectApplyAuraName3, Amp: t.EffectAmplitude3),
        };

        // Прямой эффект: приоритет хил > школьный урон > урон оружия (мили-абилка); иначе без числа.
        static bool IsWeapon(int eff) => eff is EffectWeaponDamage or EffectNormalizedWeaponDmg
            or EffectWeaponDamageNoSchool or EffectWeaponPercentDamage;
        var heal = Array.Find(effects, e => e.Eff == EffectHeal);
        var dmg = Array.Find(effects, e => e.Eff == EffectSchoolDamage);
        var weapon = Array.Find(effects, e => IsWeapon(e.Eff));
        var isHeal = heal.Eff == EffectHeal;
        var chosen = isHeal ? heal : dmg.Eff != 0 ? dmg : weapon;
        var isWeapon = IsWeapon(chosen.Eff);

        int min = 0, max = 0;
        uint weaponPercent = 0;
        if (chosen.Eff == EffectWeaponPercentDamage)
            weaponPercent = (uint)Math.Max(0, chosen.Bp); // BasePoints = % урона оружия
        else if (chosen.Eff != 0)
        {
            // CMaNGOS: value = (BasePoints+1) .. (BasePoints+DieSides). DieSides<=1 → фиксированная величина.
            min = chosen.Bp + 1;
            max = chosen.Ds > 1 ? chosen.Bp + chosen.Ds : min;
            if (max < min)
                max = min;
        }

        var cooldown = (int)Math.Max(t.RecoveryTime, t.CategoryRecoveryTime);
        // PowerType: 0=мана (стоимость флэт или % базовой), 1=ярость, 3=энергия (стоимость флэт в единицах
        // ресурса; ярость в DBC уже ×10, как у нас). Health-кост (-2) → без стоимости (Math.Max 0). M10.4a.
        var powerType = (byte)Math.Max(0, t.PowerType);
        var manaPct = powerType == 0 ? t.ManaCostPercentage : 0;

        // Периодическая аура (DoT/HoT, M10.4b): APPLY_AURA с типом PERIODIC_DAMAGE/HEAL → тик во времени.
        var periodic = Array.Find(effects, e => e.Eff == EffectApplyAura
            && e.Aura is AuraPeriodicDamage or AuraPeriodicHeal);
        var isPeriodic = periodic.Eff == EffectApplyAura;
        var periodicHeal = periodic.Aura == AuraPeriodicHeal;
        var tickAmount = isPeriodic ? periodic.Bp + 1 : 0;          // CMaNGOS: BasePoints+1 за тик
        var tickInterval = isPeriodic ? periodic.Amp : 0;

        // Непериодическая аура (бафф/дебафф, M10.4c): прочий APPLY_AURA. Бафф/дебафф различаем по знаку
        // BasePoints (>=0 — бафф на себя; <0 — дебафф на цель-существо) — надёжнее enum-целей. Простой
        // механический эффект — только MOD_INCREASE_HEALTH (+макс. HP); прочие стат-моды пока визуальны.
        var auraBuffEff = Array.Find(effects, e => e.Eff == EffectApplyAura
            && e.Aura is not (AuraPeriodicDamage or AuraPeriodicHeal) && e.Aura != 0);
        var auraBuff = auraBuffEff.Eff == EffectApplyAura;
        var auraPositive = auraBuff && auraBuffEff.Bp >= 0;
        var hpAura = Array.Find(effects, e => e.Eff == EffectApplyAura && e.Aura == AuraModIncreaseHealth);
        var healthBonus = hpAura.Eff == EffectApplyAura ? hpAura.Bp + 1 : 0;

        var auraDuration = isPeriodic || auraBuff ? SpellDurations.Get(t.DurationIndex) : 0;

        // M7 #33: движущий эффект. Charge(96)=рывок (сплайн); Leap(29)=прыжок вперёд (Blink);
        // TeleportUnits(5)=телепорт за спину цели (триггер Shadowstep 36563). TRIGGER_SPELL(64) — цепочка
        // (Shadowstep 36554 → 36563): несём id триггера, тип движения резолвится у триггера в SpellCastCompletion.
        var movement =
            Array.Exists(effects, e => e.Eff == EffectCharge) ? SpellMovement.Charge :
            Array.Exists(effects, e => e.Eff == EffectLeap) ? SpellMovement.TeleportForward :
            Array.Exists(effects, e => e.Eff == EffectTeleportUnits) ? SpellMovement.TeleportBehind :
            SpellMovement.None;
        uint triggerSpell =
            t.Effect1 == EffectTriggerSpell ? (uint)t.EffectTriggerSpell1 :
            t.Effect2 == EffectTriggerSpell ? (uint)t.EffectTriggerSpell2 :
            t.Effect3 == EffectTriggerSpell ? (uint)t.EffectTriggerSpell3 : 0u;

        // M11.3: создание предмета (крафт). count = BasePoints+1 (Smelt Bronze: BasePoints=1 → 2 слитка).
        uint createItemId = 0, createItemCount = 0;
        if (t.Effect1 == EffectCreateItem) { createItemId = t.EffectItemType1; createItemCount = (uint)Math.Max(1, t.EffectBasePoints1 + 1); }
        else if (t.Effect2 == EffectCreateItem) { createItemId = t.EffectItemType2; createItemCount = (uint)Math.Max(1, t.EffectBasePoints2 + 1); }
        else if (t.Effect3 == EffectCreateItem) { createItemId = t.EffectItemType3; createItemCount = (uint)Math.Max(1, t.EffectBasePoints3 + 1); }

        List<(uint Item, uint Count)>? reagents = null;
        AddReagent(ref reagents, t.Reagent1, t.ReagentCount1);
        AddReagent(ref reagents, t.Reagent2, t.ReagentCount2);
        AddReagent(ref reagents, t.Reagent3, t.ReagentCount3);
        AddReagent(ref reagents, t.Reagent4, t.ReagentCount4);
        AddReagent(ref reagents, t.Reagent5, t.ReagentCount5);
        AddReagent(ref reagents, t.Reagent6, t.ReagentCount6);
        AddReagent(ref reagents, t.Reagent7, t.ReagentCount7);
        AddReagent(ref reagents, t.Reagent8, t.ReagentCount8);

        return new SpellInfo((byte)t.SchoolMask, min, max, SpellCastTimes.Get(t.CastingTimeIndex),
            t.ManaCost, cooldown, isHeal, manaPct, t.StartRecoveryTime, powerType, isWeapon, weaponPercent,
            isPeriodic, periodicHeal, tickAmount, tickInterval, auraDuration, auraBuff, auraPositive, healthBonus,
            movement, triggerSpell, createItemId, createItemCount, reagents);
    }

    private static void AddReagent(ref List<(uint Item, uint Count)>? reagents, int item, uint count)
    {
        if (item > 0 && count > 0)
            (reagents ??= new List<(uint, uint)>()).Add(((uint)item, count));
    }

    /// <summary>
    /// Легаси-словарь эффектов (ранг 1) — фолбэк, если БД мира недоступна (M6.4). Совпадает с данными
    /// spell_template для этих id; в норме сервер берёт значения из БД.
    /// </summary>
    private static readonly Dictionary<uint, SpellInfo> LegacySpells = new()
    {
        [133] = new(SchoolFire, 14, 22, 1500, ManaCost: 30, CooldownMs: 0),     // Fireball rank 1
        [116] = new(SchoolFrost, 14, 20, 1500, ManaCost: 25, CooldownMs: 0),    // Frostbolt rank 1
        [2136] = new(SchoolFire, 24, 32, 0, ManaCost: 40, CooldownMs: 8000),    // Fire Blast rank 1 (мгновенный)
        [2050] = new(SchoolHoly, 45, 56, 1500, ManaCost: 30, CooldownMs: 0, IsHeal: true), // Lesser Heal rank 1
    };

    /// <summary>Спеллы, выдаваемые игроку в SMSG_INITIAL_SPELLS (для каста). M6.4.</summary>
    public static readonly int[] GrantedCombatSpells = { 133, 116, 2136, 2050 };

    // Группы эксклюзивных переключателей (M7 #21): один активен в группе.
    public const byte GroupShapeshift = 1;   // стойки воина / формы друида
    public const byte GroupPaladinAura = 2;  // ауры паладина
    public const byte GroupHunterAspect = 3; // аспекты охотника

    /// <summary>Переключатель: форма шейпшифта (0 — без формы) + группа эксклюзивности. M7 #21.</summary>
    public readonly record struct Toggle(byte Form, byte Group);

    /// <summary>
    /// Спеллы-переключатели (M6.12/M7 #21): мгновенный каст без маны/цели → перманентная аура (персист).
    /// Форма (стойки воина → панель стоек). Эксклюзивны в группе. ⚠️ Только РАНГ 1 — высшие ранги имеют
    /// другие spell-id (высшие ранги/формы друида — расширение системы аур).
    /// </summary>
    private static readonly Dictionary<uint, Toggle> Toggles = new()
    {
        // Стойки воина (форма → панель стоек): Battle=17, Defensive=18, Berserker=19.
        [2457] = new(17, GroupShapeshift),
        [71] = new(18, GroupShapeshift),
        [2458] = new(19, GroupShapeshift),
        // Ауры паладина (эксклюзивны).
        [465] = new(0, GroupPaladinAura),    // Devotion Aura
        [7294] = new(0, GroupPaladinAura),   // Retribution Aura
        [19746] = new(0, GroupPaladinAura),  // Concentration Aura
        [32223] = new(0, GroupPaladinAura),  // Crusader Aura
        [19876] = new(0, GroupPaladinAura),  // Shadow Resistance Aura
        [19888] = new(0, GroupPaladinAura),  // Frost Resistance Aura
        [19891] = new(0, GroupPaladinAura),  // Fire Resistance Aura
        // Аспекты охотника (эксклюзивны).
        [13165] = new(0, GroupHunterAspect), // Aspect of the Hawk
        [5118] = new(0, GroupHunterAspect),  // Aspect of the Cheetah
        [13163] = new(0, GroupHunterAspect), // Aspect of the Monkey
        [13159] = new(0, GroupHunterAspect), // Aspect of the Pack
        [20043] = new(0, GroupHunterAspect), // Aspect of the Wild
        [13161] = new(0, GroupHunterAspect), // Aspect of the Beast
        [34074] = new(0, GroupHunterAspect), // Aspect of the Viper
        [61846] = new(0, GroupHunterAspect), // Aspect of the Dragonhawk
    };

    /// <summary>Переключатель (стойка/аура/аспект) по id (true — это переключатель).</summary>
    public static bool TryGetToggle(uint spellId, out Toggle toggle) => Toggles.TryGetValue(spellId, out toggle);
}
