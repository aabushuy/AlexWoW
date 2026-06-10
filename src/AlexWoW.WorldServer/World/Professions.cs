using AlexWoW.Database.Models;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Справочник профессий (M11.2/M11.5). Маппинг «спелл → навык» берём из РЕАЛЬНОГО spell_template:
/// спелл, изучающий профессию, несёт эффект SKILL(118)/SKILL_STEP(44) с id навыка в EffectMiscValue.
/// Курируем только небольшой стабильный набор id профессиональных навыков (SkillLine.dbc нет в дампе).
/// </summary>
public static class Professions
{
    // SpellEffect, выдающие/повышающие навык (3.3.5a): SKILL=118, SKILL_STEP=44.
    private const int EffectSkill = 118;
    private const int EffectSkillStep = 44;
    // LEARN_SPELL=36: спелл-«учитель» (напр. «Подмастерье кузнеца» 2020) учит реальный спелл —
    // открывашку окна профессии (2018 «Кузнечное дело», эффект TRADE_SKILL). EffectTriggerSpell = id.
    private const int EffectLearnSpell = 36;

    /// <summary>
    /// Если спелл — «учитель» профессии (Effect LEARN_SPELL=36), возвращает id изучаемого им спелла
    /// (открывашка окна/навык), иначе 0. Сам учитель в книге не показывается. M11 (#2).
    /// </summary>
    public static uint TaughtSpell(SpellTemplateData t)
    {
        if (t.Effect1 == EffectLearnSpell && t.EffectTriggerSpell1 > 0) return (uint)t.EffectTriggerSpell1;
        if (t.Effect2 == EffectLearnSpell && t.EffectTriggerSpell2 > 0) return (uint)t.EffectTriggerSpell2;
        if (t.Effect3 == EffectLearnSpell && t.EffectTriggerSpell3 > 0) return (uint)t.EffectTriggerSpell3;
        return 0;
    }

    /// <summary>Потолок навыка апрентис-тира (стартовый при изучении профессии). M11.2.</summary>
    public const ushort ApprenticeMax = 75;

    /// <summary>Потолки навыка по тирам: [0]=нет, апрентис 75 … грандмастер 450. M11.5.</summary>
    public static readonly ushort[] TierMax = { 0, 75, 150, 225, 300, 375, 450 };

    /// <summary>Линии профессиональных и вторичных навыков (SkillLine.dbc id). Стабильный набор.</summary>
    public static readonly IReadOnlySet<ushort> SkillLines = new HashSet<ushort>
    {
        164, // Blacksmithing — кузнечное
        165, // Leatherworking — кожевничество
        171, // Alchemy — алхимия
        182, // Herbalism — травничество
        185, // Cooking — кулинария
        186, // Mining — горное дело
        197, // Tailoring — портняжное
        202, // Engineering — инженерное
        333, // Enchanting — наложение чар
        356, // Fishing — рыбная ловля
        393, // Skinning — снятие шкур
        755, // Jewelcrafting — ювелирное
        773, // Inscription — начертание
        129, // First Aid — первая помощь
    };

    /// <summary>Рецепт: к какому навыку относится крафт-спелл и его требуемый (оранжевый) уровень. M11.3/M11.5.</summary>
    public readonly record struct Recipe(ushort SkillId, ushort ReqSkill);

    /// <summary>
    /// Курированный сид рецептов крафта (spellId → навык + req). skill_line_ability нет в дампе, поэтому
    /// привязку рецепт→навык для прокачки задаём здесь (стартовые профессии). Сам крафт (расход реагентов +
    /// создание предмета) работает для ЛЮБОГО create-item спелла из spell_template; сид нужен только для skill-up.
    /// </summary>
    public static readonly IReadOnlyDictionary<uint, Recipe> Recipes = new Dictionary<uint, Recipe>
    {
        // Горное дело (плавка): руда → слиток.
        [2657] = new(186, 1),    // Smelt Copper
        [3304] = new(186, 65),   // Smelt Tin
        [2659] = new(186, 50),   // Smelt Bronze
        // Кузнечное.
        [2660] = new(164, 1),    // Rough Sharpening Stone
        [3115] = new(164, 1),    // Rough Weightstone
        [12260] = new(164, 1),   // Rough Copper Vest
        // Кулинария / алхимия (демо-рецепты).
        [2538] = new(185, 1),    // Charred Wolf Meat
        [7836] = new(171, 1),    // Blackmouth Oil
    };

    /// <summary>Нода сбора: навык, требуемый уровень и выдаваемый ресурс (диапазон количества). M11.4.</summary>
    public readonly record struct GatherNode(ushort SkillId, ushort ReqSkill, uint Item, uint MinCount, uint MaxCount);

    /// <summary>
    /// Курированный сид нод сбора (gameobject entry → навык/req/ресурс). Lock.dbc (требуемый скилл ноды)
    /// нет в дампе, поэтому задаём здесь. Используется и для гейта при использовании, и для skill-up.
    /// </summary>
    public static readonly IReadOnlyDictionary<uint, GatherNode> Nodes = new Dictionary<uint, GatherNode>
    {
        // Горное дело (186): рудные жилы → руда.
        [1731] = new(186, 1, 2770, 1, 2),    // Copper Vein → Copper Ore
        [2055] = new(186, 1, 2770, 1, 2),    // Copper Vein (альт. entry)
        [1732] = new(186, 65, 2771, 1, 2),   // Tin Vein → Tin Ore
        [1733] = new(186, 75, 2775, 1, 2),   // Silver Vein → Silver Ore
        [1735] = new(186, 125, 2772, 1, 2),  // Iron Deposit → Iron Ore
        // Травничество (182): травы.
        [1617] = new(182, 1, 765, 1, 2),     // Silverleaf
        [1618] = new(182, 1, 2447, 1, 2),    // Peacebloom
        [1619] = new(182, 15, 2449, 1, 2),   // Earthroot
        [1620] = new(182, 50, 785, 1, 2),    // Mageroyal
    };

    /// <summary>Русское имя навыка профессии (для сообщений «требуется …»). M11.4.</summary>
    public static string SkillName(ushort skillId) => skillId switch
    {
        164 => "кузнечное дело",
        165 => "кожевничество",
        171 => "алхимия",
        182 => "травничество",
        185 => "кулинария",
        186 => "горное дело",
        197 => "портняжное дело",
        202 => "инженерное дело",
        333 => "наложение чар",
        356 => "рыбная ловля",
        393 => "снятие шкур",
        755 => "ювелирное дело",
        773 => "начертание",
        129 => "первая помощь",
        _ => "навык",
    };

    /// <summary>
    /// Шанс (%) поднять навык на +1 за действие (крафт/сбор), по «цвету» относительно требуемого уровня
    /// (CMaNGOS SkillGainChance: оранжевый/жёлтый/зелёный/серый). diff = текущий − требуемый. M11.5.
    /// </summary>
    public static int SkillUpChance(ushort value, ushort reqSkill)
    {
        var diff = value - reqSkill;
        if (diff < 25) return 100;  // оранжевый — всегда
        if (diff < 50) return 75;   // жёлтый
        if (diff < 75) return 35;   // зелёный
        return 0;                   // серый — не растёт
    }

    /// <summary>
    /// Доп. спеллы, выдаваемые вместе с навыком профессии (M11.2 фикс). Горное дело (186) само по себе —
    /// только сбор (спелл 2575, эффект 33), а окно ПЛАВКИ открывает отдельный спелл «Выплавка» (2656,
    /// эффект TRADE_SKILL): на оффе он выдаётся вместе с Mining. Прочие профессии (кузнечное/алхимия/…)
    /// сами открывают окно (их учебный спелл имеет эффект 47), доп. спелл не нужен.
    /// </summary>
    public static readonly IReadOnlyDictionary<ushort, uint[]> AutoGrantSpells = new Dictionary<ushort, uint[]>
    {
        [186] = [2656], // Mining → Smelting (окно плавки)
    };

    /// <summary>Что выдаёт спелл-профессия: навык и его потолок (тир). M11.2/M11.5.</summary>
    public readonly record struct SkillGrant(ushort SkillId, ushort Max);

    /// <summary>
    /// Навык и потолок, которые выдаёт спелл-профессия (Effect SKILL/SKILL_STEP), либо null. Тир кодируется
    /// в EffectBasePoints: BP=0 → апрентис (75), BP=2 → подмастерье (150) … BP=6 → грандмастер (450).
    /// Формула: max = clamp(max(1, BP) × 75, 75, 450).
    /// </summary>
    public static SkillGrant? SkillGrantedBy(SpellTemplateData t)
    {
        ReadOnlySpan<(int Eff, int Misc, int Bp)> effects =
        [
            (t.Effect1, t.EffectMiscValue1, t.EffectBasePoints1),
            (t.Effect2, t.EffectMiscValue2, t.EffectBasePoints2),
            (t.Effect3, t.EffectMiscValue3, t.EffectBasePoints3),
        ];
        foreach (var (eff, misc, bp) in effects)
            if ((eff == EffectSkill || eff == EffectSkillStep) && misc > 0 && SkillLines.Contains((ushort)misc))
            {
                var max = (ushort)Math.Clamp(Math.Max(1, bp) * 75, ApprenticeMax, TierMax[^1]);
                return new SkillGrant((ushort)misc, max);
            }
        return null;
    }
}
