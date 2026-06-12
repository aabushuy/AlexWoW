using System.Collections.Concurrent;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Каталог спеллов (M6.4 → M10.2, DI-синглтон M7 S3): данные эффекта/каста для оркестрации
/// (<see cref="Handlers.SpellCastService"/>).
/// <para>M10.2: единственный источник — дамп <c>spell_template</c> (mangos): школа, время каста
/// (<see cref="SpellCastTimes"/>), эффект (урон/хил), мана (флэт или % базовой), кулдаун — читаются из БД
/// и кэшируются (данные спелла иммутабельны). Хардкод снят (легаси-стартер M6.4 убран).</para>
/// Переключатели (<see cref="Toggles"/>) — наша игровая конфигурация, не из БД (чистые данные — статические члены).
/// </summary>
public sealed class SpellCatalog(IWorldRepository worldDb, ILogger<SpellCatalog> logger)
{
    /// <summary>Семейство спеллов воина (SpellFamilyName, CMaNGOS SPELLFAMILY_WARRIOR). M10.6.</summary>
    private const uint SpellFamilyWarrior = 4;

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
    private const int EffectEnergize = 30;               // начисление ресурса (MiscValue = power type) — M10.6
    private const int EffectDummy = 3;                   // dummy — скриптовый эффект (ярость Рывка) — M10.6
    // AuraType (EffectApplyAuraName*, CMaNGOS): периодический урон/хил + простой бонус к HP.
    private const int AuraPeriodicDamage = 3;
    private const int AuraPeriodicHeal = 8;
    private const int AuraModIncreaseHealth = 34;        // +макс. HP (простой эффект баффа, M10.4c)
    private const int AuraModBlockPercent = 51;          // +% блока (напр. «Блок щитом»)
    private const int AuraModDamagePercentTaken = 87;    // % получаемого урона (напр. «Глухая оборона», отрицательный)
    // CC-ауры (SpellAuraDefines.h): контроль цели. MiscValue не нужен — тип определяем по самой ауре.
    private const int AuraModConfuse = 5;                // дезориентация (Polymorph/Blind)
    private const int AuraModFear = 7;                   // страх (Psychic Scream/Fear)
    private const int AuraModStun = 12;                  // оглушение (Hammer of Justice/Concussion Blow)
    private const int AuraModRoot = 26;                  // обездвиживание (Frost Nova/Entangling Roots)
    private const int AuraModSilence = 27;               // немота (Strangulate/Silence)

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
        bool AuraBuff = false, bool AuraPositive = false, int HealthBonus = 0, int BlockBonus = 0,
        int DamageTakenPct = 0,
        SpellMovement Movement = SpellMovement.None, uint TriggerSpellId = 0,
        uint CreateItemId = 0, uint CreateItemCount = 0,
        IReadOnlyList<(uint Item, uint Count)>? Reagents = null,
        // M10.6: семейство/маска принадлежности — матчинг модификаторами талантов (SpellModifiers.IsAffected);
        // индексы эффектов (1..3, 0 — нет) — адресация SPELLMOD_EFFECT{N} к прямому/периодическому эффекту.
        uint FamilyName = 0, ulong FamilyFlags = 0, uint FamilyFlags2 = 0,
        byte DirectEffectIndex = 0, byte PeriodicEffectIndex = 0,
        // M10.6: начисление ресурса кастеру (ENERGIZE / ярость Рывка): величина, power type, индекс эффекта.
        uint EnergizeAmount = 0, byte EnergizePower = 0, byte EnergizeEffectIndex = 0,
        // Фаза 2 CC: тип контроля (стан/рут/страх/немота/дезориентация) + длительность (мс). None — не CC.
        CrowdControlKind CrowdControl = CrowdControlKind.None, int CrowdControlMs = 0);

    /// <summary>Вид контроля (CC, Фаза 2): по типу CC-ауры спелла. None — не контроль.</summary>
    public enum CrowdControlKind : byte { None = 0, Stun = 1, Root = 2, Fear = 3, Silence = 4, Disorient = 5 }

    /// <summary>Движущий эффект спелла (M7 #33): рывок к цели (сплайн), телепорт вперёд (Blink) или за спину
    /// цели (Shadowstep). None — не двигает.</summary>
    public enum SpellMovement : byte { None = 0, Charge = 1, TeleportForward = 2, TeleportBehind = 3 }

    /// <summary>Кэш разобранных спеллов (включая «нет в БД» = null), данные иммутабельны. M10.2.</summary>
    private readonly ConcurrentDictionary<uint, SpellInfo?> _cache = new();

    /// <summary>
    /// Эффект спелла из <c>spell_template</c> (с кэшем). Возвращает null, если спелл не найден в БД мира
    /// или БД недоступна (клиент сам валидирует — каст без эффекта).
    /// </summary>
    public async Task<SpellInfo?> GetAsync(uint spellId, CancellationToken ct)
    {
        if (_cache.TryGetValue(spellId, out var cached))
            return cached;
        try
        {
            var tpl = await worldDb.GetSpellAsync(spellId, ct);
            var info = tpl is not null ? FromTemplate(tpl) : null;
            _cache[spellId] = info; // кэшируем определённый результат (в т.ч. null = нет такого спелла)
            return info;
        }
        catch (Exception ex)
        {
            // БД мира недоступна / ошибка маппинга — спелл без эффекта (без кэша). ЛОГИРУЕМ.
            logger.LogError(ex, "SpellCatalog: spell={Spell} — ошибка чтения spell_template", spellId);
            return null;
        }
    }

    /// <summary>Маппинг строки spell_template → наша модель эффекта (M10.2). internal — покрыт автотестом M12.7.</summary>
    internal static SpellInfo FromTemplate(SpellTemplateData t)
    {
        var effects = new[]
        {
            (Eff: t.Effect1, Bp: t.EffectBasePoints1, Ds: t.EffectDieSides1, Aura: t.EffectApplyAuraName1, Amp: t.EffectAmplitude1),
            (Eff: t.Effect2, Bp: t.EffectBasePoints2, Ds: t.EffectDieSides2, Aura: t.EffectApplyAuraName2, Amp: t.EffectAmplitude2),
            (Eff: t.Effect3, Bp: t.EffectBasePoints3, Ds: t.EffectDieSides3, Aura: t.EffectApplyAuraName3, Amp: t.EffectAmplitude3),
        };

        // Прямой эффект: приоритет хил > школьный урон > урон оружия (мили-абилка); иначе без числа.
        // Индекс выбранного эффекта (0-базный; −1 — нет) нужен модификаторам SPELLMOD_EFFECT{N} (M10.6).
        static bool IsWeapon(int eff) => eff is EffectWeaponDamage or EffectNormalizedWeaponDmg
            or EffectWeaponDamageNoSchool or EffectWeaponPercentDamage;
        var healIdx = Array.FindIndex(effects, e => e.Eff == EffectHeal);
        var dmgIdx = Array.FindIndex(effects, e => e.Eff == EffectSchoolDamage);
        var weaponIdx = Array.FindIndex(effects, e => IsWeapon(e.Eff));
        var isHeal = healIdx >= 0;
        var chosenIdx = isHeal ? healIdx : dmgIdx >= 0 ? dmgIdx : weaponIdx;
        var chosen = chosenIdx >= 0 ? effects[chosenIdx] : default;
        var isWeapon = IsWeapon(chosen.Eff);

        int min = 0, max = 0;
        uint weaponPercent = 0;
        if (chosen.Eff == EffectWeaponPercentDamage)
        {
            weaponPercent = (uint)Math.Max(0, chosen.Bp); // BasePoints = % урона оружия
        }
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
        var periodicIdx = Array.FindIndex(effects, e => e.Eff == EffectApplyAura
            && e.Aura is AuraPeriodicDamage or AuraPeriodicHeal);
        var periodic = periodicIdx >= 0 ? effects[periodicIdx] : default;
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
        var hpAura = Array.Find(effects, e => e.Eff == EffectApplyAura && e.Aura == AuraModIncreaseHealth);
        var healthBonus = hpAura.Eff == EffectApplyAura ? hpAura.Bp + 1 : 0;
        // +% блока (MOD_BLOCK_PERCENT, напр. «Блок щитом»): величина = BasePoints+1.
        var blockAura = Array.Find(effects, e => e.Eff == EffectApplyAura && e.Aura == AuraModBlockPercent);
        var blockBonus = blockAura.Eff == EffectApplyAura ? blockAura.Bp + 1 : 0;
        // % получаемого урона (MOD_DAMAGE_PERCENT_TAKEN, напр. «Глухая оборона»): отрицательный = снижение.
        var dmgTakenAura = Array.Find(effects, e => e.Eff == EffectApplyAura && e.Aura == AuraModDamagePercentTaken);
        var damageTakenPct = dmgTakenAura.Eff == EffectApplyAura ? dmgTakenAura.Bp + 1 : 0;
        // Бафф/дебафф: по знаку BasePoints, НО защитный само-бафф со снижением урона (−% получаемого)
        // — положительный (на себя), несмотря на отрицательный Bp («Глухая оборона»).
        var auraPositive = auraBuff && (auraBuffEff.Bp >= 0 || damageTakenPct < 0);
        // Брони (эксклюзивные само-баффы) — положительны по определению, даже если первый аура-эффект —
        // прок-триггер с отрицательным BasePoints (Molten Armor: PROC_TRIGGER_SPELL Bp=−1 → иначе движок
        // принял бы за дебафф и не наложил бы без цели). Frost/Mage/Demon/Fel уже положительны (MOD_RESISTANCE+).
        if (auraBuff && !auraPositive && ExclusiveAuras.ContainsKey((uint)t.Id))
            auraPositive = true;

        var auraDuration = isPeriodic || auraBuff ? SpellDurations.Get(t.DurationIndex) : 0;

        // Фаза 2 CC: тип контроля по первой найденной CC-ауре (стан/рут/страх/немота/дезориентация).
        // Длительность — из DurationIndex (даже если спелл не помечен auraBuff/periodic — CC всегда временный).
        var ccEff = Array.Find(effects, e => e.Eff == EffectApplyAura
            && e.Aura is AuraModStun or AuraModRoot or AuraModFear or AuraModSilence or AuraModConfuse);
        var crowdControl = ccEff.Eff != EffectApplyAura ? CrowdControlKind.None : ccEff.Aura switch
        {
            AuraModStun => CrowdControlKind.Stun,
            AuraModRoot => CrowdControlKind.Root,
            AuraModFear => CrowdControlKind.Fear,
            AuraModSilence => CrowdControlKind.Silence,
            _ => CrowdControlKind.Disorient, // AuraModConfuse
        };
        var crowdControlMs = crowdControl != CrowdControlKind.None ? SpellDurations.Get(t.DurationIndex) : 0;

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

        // M10.6: начисление ресурса кастеру. Общий случай — SPELL_EFFECT_ENERGIZE (30): MiscValue = power
        // type, величина = BasePoints+1. Спец-случай — ярость Рывка воина: она закодирована DUMMY-эффектом
        // (BasePoints 89/119/149 → 9/12/15 ярости ×10), который ядра скриптуют (TrinityCore spell_warr_charge,
        // CMaNGOS Spell::EffectDummy) — распознаём по семейству воина + эффекту CHARGE.
        uint energizeAmount = 0;
        byte energizePower = 0, energizeIdx = 0;
        var energizeEff = Array.FindIndex(effects, e => e.Eff == EffectEnergize);
        if (energizeEff >= 0)
        {
            var misc = energizeEff switch { 0 => t.EffectMiscValue1, 1 => t.EffectMiscValue2, _ => t.EffectMiscValue3 };
            energizeAmount = (uint)Math.Max(0, effects[energizeEff].Bp + 1);
            energizePower = (byte)Math.Max(0, misc);
            energizeIdx = (byte)(energizeEff + 1);
        }
        else if (t.SpellFamilyName == SpellFamilyWarrior && Array.Exists(effects, e => e.Eff == EffectCharge))
        {
            var dummy = Array.FindIndex(effects, e => e.Eff == EffectDummy && e.Bp > 0);
            if (dummy >= 0)
            {
                energizeAmount = (uint)(effects[dummy].Bp + 1);
                energizePower = 1; // ярость (уже ×10 в DBC, как у нас)
                energizeIdx = (byte)(dummy + 1);
            }
        }

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
            blockBonus, damageTakenPct, movement, triggerSpell, createItemId, createItemCount, reagents,
            t.SpellFamilyName, t.SpellFamilyFlags, t.SpellFamilyFlags2,
            (byte)(chosenIdx + 1), (byte)(periodicIdx + 1),
            energizeAmount, energizePower, energizeIdx,
            crowdControl, crowdControlMs);
    }

    private static void AddReagent(ref List<(uint Item, uint Count)>? reagents, int item, uint count)
    {
        if (item > 0 && count > 0)
            (reagents ??= []).Add(((uint)item, count));
    }

    // Группы эксклюзивных переключателей (M7 #21): один активен в группе.
    public const byte GroupShapeshift = 1;   // стойки воина / формы друида
    public const byte GroupPaladinAura = 2;  // ауры паладина
    public const byte GroupHunterAspect = 3; // аспекты охотника
    public const byte GroupMageArmor = 4;    // брони мага (Frost/Ice/Mage/Molten — взаимоисключающие)
    public const byte GroupWarlockArmor = 5; // брони чернокнижника (Demon Skin/Demon Armor/Fel Armor)
    public const byte GroupDkPresence = 6;   // присутствия DK (Blood/Frost/Unholy — не шейпшифт, эксклюзивны)
    public const byte GroupPaladinSeal = 7;  // печати паладина (Righteousness/Light/Wisdom/Justice — взаимоисключающие)

    /// <summary>Переключатель: форма шейпшифта (0 — без формы) + группа эксклюзивности. M7 #21.</summary>
    public readonly record struct Toggle(byte Form, byte Group);

    /// <summary>
    /// Спеллы-переключатели (M6.12/M7 #21): мгновенный каст без маны/цели → перманентная аура (персист).
    /// Форма (стойки/Stealth/Shadowform/Ghost Wolf → панель формы). Эксклюзивны в группе. Для многоранговых
    /// (Stealth) перечисляем ВСЕ ранги — на 80-м кастуется высший, но игрок может применить любой.
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
        // Формы-шейпшифты (одна на класс → общая группа GroupShapeshift; форма из EffectMiscValue ауры 36).
        [1784] = new(30, GroupShapeshift),   // Stealth (рога) — ранг 1
        [1785] = new(30, GroupShapeshift),   // Stealth ранг 2
        [1786] = new(30, GroupShapeshift),   // Stealth ранг 3
        [1787] = new(30, GroupShapeshift),   // Stealth ранг 4
        [15473] = new(28, GroupShapeshift),  // Shadowform (жрец)
        [2645] = new(16, GroupShapeshift),   // Ghost Wolf (шаман)
        // Присутствия DK (не шейпшифт, форма 0; эксклюзивны как ауры паладина).
        [48266] = new(0, GroupDkPresence),   // Blood Presence
        [48263] = new(0, GroupDkPresence),   // Frost Presence
        [48265] = new(0, GroupDkPresence),   // Unholy Presence
    };

    /// <summary>
    /// Расширение рангов (заполняется на старте из БД, <see cref="ExpandRankTogglesAsync"/>): seed-таблицы
    /// <see cref="Toggles"/>/<see cref="ExclusiveAuras"/> держат по одному рангу на абилку, а игрок (после
    /// all-ranks .learnall) кастует ВЫСШИЙ ранг. Сюда подтягиваются все одноимённые ранги с тем же form/group.
    /// </summary>
    private static readonly ConcurrentDictionary<uint, Toggle> ToggleRankExpansion = new();
    private static readonly ConcurrentDictionary<uint, byte> ExclusiveRankExpansion = new();

    /// <summary>Переключатель (стойка/аура/аспект/форма) по id — seed-таблица ИЛИ расширение рангов.</summary>
    public static bool TryGetToggle(uint spellId, out Toggle toggle)
        => Toggles.TryGetValue(spellId, out toggle) || ToggleRankExpansion.TryGetValue(spellId, out toggle);

    /// <summary>
    /// Подтягивает из БД все одноимённые ранги seed-переключателей и эксклюзивных аур (с тем же form/group).
    /// Игрок кастует высший ранг, которого нет в seed-таблице → без этого toggle/эксклюзив у многоранговых
    /// аур/аспектов/броней не срабатывает. Зовётся один раз на старте world-цикла (БД уже доступна).
    /// </summary>
    public async Task ExpandRankTogglesAsync(CancellationToken ct = default)
    {
        try
        {
            var seeds = Toggles.Keys.Concat(ExclusiveAuras.Keys).ToList();
            foreach (var (rankId, seedId) in await worldDb.GetSameNameRankIdsAsync(seeds, ct))
            {
                if (Toggles.TryGetValue(seedId, out var tog))
                    ToggleRankExpansion[rankId] = tog;
                if (ExclusiveAuras.TryGetValue(seedId, out var grp))
                    ExclusiveRankExpansion[rankId] = grp;
            }
            logger.LogInformation("Расширение рангов toggle: +{T} переключателей, +{E} эксклюзивных аур",
                ToggleRankExpansion.Count, ExclusiveRankExpansion.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Расширение рангов toggle пропущено: {Msg}", ex.Message);
        }
    }

    /// <summary>
    /// Эксклюзивные группы для ПЛАТНЫХ временны́х само-баффов (Фаза 2 — формы/toggle): брони мага и
    /// чернокнижника. В отличие от <see cref="Toggles"/> (бесплатные мгновенные переключатели), эти спеллы
    /// идут обычным кастом (мана + длительность 30 мин) через <see cref="Handlers.PeriodicsService.ApplyAuraEffectAsync"/>,
    /// но эксклюзивны: новая броня снимает прежнюю той же группы. Все ранги — на 80-м кастуется максимальный,
    /// но по ходу прокачки игрок применяет разные ранги.
    /// </summary>
    private static readonly Dictionary<uint, byte> ExclusiveAuras = BuildExclusiveAuras();

    private static Dictionary<uint, byte> BuildExclusiveAuras()
    {
        var map = new Dictionary<uint, byte>();
        // Брони мага (Frost/Ice/Mage/Molten — взаимоисключающие): все ранги одной группой.
        foreach (var id in new uint[]
        {
            168, 7300, 7301,                          // Frost Armor
            7302, 7320, 10219, 10220, 27124, 43008,   // Ice Armor
            6117, 22782, 22783, 27125, 43023, 43024,  // Mage Armor
            30482, 34913, 43043, 43044, 43045, 43046, // Molten Armor
        })
            map[id] = GroupMageArmor;
        // Брони чернокнижника (Demon Skin/Demon Armor/Fel Armor — взаимоисключающие): все ранги одной группой.
        foreach (var id in new uint[]
        {
            687, 696,                                          // Demon Skin
            706, 1086, 11733, 11734, 11735, 27260, 47793, 47889, // Demon Armor
            28176, 28189, 44520, 44977, 47892, 47893,         // Fel Armor
        })
            map[id] = GroupWarlockArmor;
        // Печати паладина (база тренера; взаимоисключающие): Righteousness/Light/Wisdom/Justice. Платный
        // timed-бафф 30 мин — путь ExclusiveAuras. On-hit прок (тип 42 → trigger) — отдельным шагом.
        foreach (var id in new uint[] { 21084, 20165, 20166, 20164 })
            map[id] = GroupPaladinSeal;
        return map;
    }

    /// <summary>Эксклюзивная группа платного само-баффа (броня/печать) по id — seed ИЛИ расширение рангов; 0 — нет.</summary>
    public static byte ExclusiveAuraGroup(uint spellId)
        => ExclusiveAuras.TryGetValue(spellId, out var g) ? g : ExclusiveRankExpansion.GetValueOrDefault(spellId);
}
