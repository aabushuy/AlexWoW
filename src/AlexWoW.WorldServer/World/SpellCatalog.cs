using System.Collections.Concurrent;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Каталог спеллов (M6.4 → M10.2): данные эффекта/каста для оркестрации (<see cref="Handlers.SpellCaster"/>).
/// <para>M10.2: основной источник — дамп <c>spell_template</c> (mangos): школа, время каста
/// (<see cref="SpellCastTimes"/>), эффект (урон/хил), мана (флэт или % базовой), кулдаун — читаются из БД
/// и кэшируются (данные спелла иммутабельны). Хардкод снят. Легаси-словарь <see cref="LegacySpells"/>
/// оставлен только фолбэком, если БД мира недоступна.</para>
/// Переключатели (<see cref="Toggles"/>) и стартовый набор (<see cref="GrantedCombatSpells"/>) — наша
/// игровая конфигурация, не из БД.
/// </summary>
public static class SpellCatalog
{
    /// <summary>МАСКИ школ магии (SpellSchoolMask, u8): Fire=0x4, Frost=0x10, Holy=0x2 (см. SMSG_*DAMAGELOG).</summary>
    public const byte SchoolFire = 0x04;
    public const byte SchoolFrost = 0x10;
    public const byte SchoolHoly = 0x02;

    // SpellEffects (Spell.dbc Effect*): прямой урон школы и прямой хил — единственные, что даёт эффект M10.2.
    private const int EffectSchoolDamage = 2;
    private const int EffectHeal = 10;

    /// <summary>
    /// Эффект спелла: школа, диапазон величины (урон ИЛИ хил), время каста (мс), стоимость маны — флэтом
    /// (<paramref name="ManaCost"/>) или процентом базовой маны (<paramref name="ManaCostPct"/>, считается у
    /// кастера), кулдаун (мс), хил-ли. Иммутабельно → кэшируется по spellId.
    /// </summary>
    public sealed record SpellInfo(byte School, int MinAmount, int MaxAmount, int CastMs, uint ManaCost,
        int CooldownMs, bool IsHeal = false, uint ManaCostPct = 0);

    /// <summary>Кэш разобранных спеллов (включая «нет в БД» = null), данные иммутабельны. M10.2.</summary>
    private static readonly ConcurrentDictionary<uint, SpellInfo?> Cache = new();

    /// <summary>
    /// Эффект спелла: из <c>spell_template</c> (с кэшем); при недоступности БД мира — легаси-фолбэк.
    /// Возвращает null, если спелл не известен ни в БД, ни в фолбэке (клиент сам валидирует — каст без эффекта).
    /// </summary>
    public static async Task<SpellInfo?> GetAsync(IWorldRepository worldDb, uint spellId,
        ILogger? logger = null, CancellationToken ct = default)
    {
        if (Cache.TryGetValue(spellId, out var cached))
            return cached;
        try
        {
            var tpl = await worldDb.GetSpellAsync(spellId, ct);
            var info = tpl is not null ? FromTemplate(tpl) : LegacySpells.GetValueOrDefault(spellId);
            Cache[spellId] = info; // кэшируем определённый результат (в т.ч. null = нет такого спелла)
            return info;
        }
        catch (Exception ex)
        {
            // БД мира недоступна / ошибка маппинга — фолбэк на легаси, без кэша. ЛОГИРУЕМ (раньше глоталось).
            logger?.LogError(ex, "SpellCatalog: spell={Spell} — ошибка чтения spell_template, фолбэк на легаси", spellId);
            return LegacySpells.GetValueOrDefault(spellId);
        }
    }

    /// <summary>Маппинг строки spell_template → наша модель эффекта (M10.2).</summary>
    private static SpellInfo FromTemplate(SpellTemplateData t)
    {
        var effects = new[]
        {
            (Eff: t.Effect1, Bp: t.EffectBasePoints1, Ds: t.EffectDieSides1),
            (Eff: t.Effect2, Bp: t.EffectBasePoints2, Ds: t.EffectDieSides2),
            (Eff: t.Effect3, Bp: t.EffectBasePoints3, Ds: t.EffectDieSides3),
        };

        // Прямой эффект: приоритет хилу, затем урону; иначе спелл без прямого эффекта (каст без числа).
        var heal = Array.Find(effects, e => e.Eff == EffectHeal);
        var dmg = Array.Find(effects, e => e.Eff == EffectSchoolDamage);
        var isHeal = heal.Eff == EffectHeal;
        var chosen = isHeal ? heal : dmg;

        int min = 0, max = 0;
        if (chosen.Eff != 0)
        {
            // CMaNGOS: value = (BasePoints+1) .. (BasePoints+DieSides). DieSides<=1 → фиксированная величина.
            min = chosen.Bp + 1;
            max = chosen.Ds > 1 ? chosen.Bp + chosen.Ds : min;
            if (max < min)
                max = min;
        }

        var cooldown = (int)Math.Max(t.RecoveryTime, t.CategoryRecoveryTime);
        // Стоимость маны учитываем только для мана-спеллов (PowerType=0). У воина/разбойника поле ManaCost —
        // это ярость/энергия (расход не-мана ресурсов абилками — M10.4), мана-гейт на них не вешаем.
        const int powerMana = 0;
        var manaFlat = t.PowerType == powerMana ? t.ManaCost : 0;
        var manaPct = t.PowerType == powerMana ? t.ManaCostPercentage : 0;
        return new SpellInfo((byte)t.SchoolMask, min, max, SpellCastTimes.Get(t.CastingTimeIndex),
            manaFlat, cooldown, isHeal, manaPct);
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
