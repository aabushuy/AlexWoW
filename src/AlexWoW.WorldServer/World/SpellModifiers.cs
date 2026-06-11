using AlexWoW.Database.Models;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Тип модификации спелла (SpellModOp, дословно из CMaNGOS <c>SpellAuraDefines.h</c>) —
/// <c>EffectMiscValue1</c> эффекта-модификатора (аура 107/108). Перечислены только применяемые нами;
/// полный список (THREAT/RANGE/CHARGES и пр.) — расширение M10.6+.
/// </summary>
public enum SpellModOp : byte
{
    Damage = 0,         // SPELLMOD_DAMAGE — итоговый урон/хил
    Duration = 1,       // SPELLMOD_DURATION — длительность ауры
    Effect1 = 3,        // SPELLMOD_EFFECT1 — величина эффекта 1 (напр. Improved Rend → тик Кровопускания)
    AllEffects = 8,     // SPELLMOD_ALL_EFFECTS — величины всех эффектов (напр. Improved Cleave)
    CastingTime = 10,   // SPELLMOD_CASTING_TIME — время каста
    Cooldown = 11,      // SPELLMOD_COOLDOWN — кулдаун
    Effect2 = 12,       // SPELLMOD_EFFECT2
    Cost = 14,          // SPELLMOD_COST — стоимость ресурса (напр. Improved Heroic Strike)
    Dot = 22,           // SPELLMOD_DOT — величина периодического тика
    Effect3 = 23,       // SPELLMOD_EFFECT3
}

/// <summary>
/// Активный модификатор спеллов игрока (M10.6): пассивный талант с аурой ADD_FLAT_MODIFIER(107) /
/// ADD_PCT_MODIFIER(108). Затрагиваемые спеллы — по семейству (<paramref name="FamilyName"/>) и 96-битной
/// маске (<paramref name="Mask1"/>=биты 0–31, <paramref name="Mask2"/>=32–63, <paramref name="Mask3"/>=64–95)
/// против SpellFamilyFlags/Flags2 цели. <paramref name="Value"/>: флэт — прибавка (ярость уже ×10);
/// процент — ±N%.
/// </summary>
public readonly record struct SpellModifier(
    uint SourceSpell, SpellModOp Op, bool IsPct, int Value,
    uint FamilyName, uint Mask1, uint Mask2, uint Mask3);

/// <summary>
/// Математика модификаторов спеллов (M10.6, чистый хелпер): извлечение из <c>spell_template</c>,
/// матчинг затронутых спеллов и применение к базовой величине. Эталон — CMaNGOS
/// <c>Aura::HandleAddModifier</c> / <c>Player::ApplySpellMod</c> / <c>SpellEntry::IsFitToFamilyMask</c>.
/// </summary>
public static class SpellModifiers
{
    // EffectApplyAuraName (CMaNGOS SpellAuraDefines.h): ауры-модификаторы спеллов.
    private const int AuraAddFlatModifier = 107; // SPELL_AURA_ADD_FLAT_MODIFIER
    private const int AuraAddPctModifier = 108;  // SPELL_AURA_ADD_PCT_MODIFIER
    private const int EffectApplyAura = 6;

    /// <summary>
    /// Извлекает эффекты-модификаторы (ауры 107/108) из строки spell_template; null — спелл не модификатор.
    /// Эффекты с пустой classmask пропускаются (нечего матчить — у валидных талантов маска всегда задана).
    /// </summary>
    public static List<SpellModifier>? ExtractFrom(SpellTemplateData t)
    {
        List<SpellModifier>? mods = null;
        Add(ref mods, t, t.Effect1, t.EffectApplyAuraName1, t.EffectBasePoints1, t.EffectMiscValue1,
            t.EffectSpellClassMask1_1, t.EffectSpellClassMask1_2, t.EffectSpellClassMask1_3);
        Add(ref mods, t, t.Effect2, t.EffectApplyAuraName2, t.EffectBasePoints2, t.EffectMiscValue2,
            t.EffectSpellClassMask2_1, t.EffectSpellClassMask2_2, t.EffectSpellClassMask2_3);
        Add(ref mods, t, t.Effect3, t.EffectApplyAuraName3, t.EffectBasePoints3, t.EffectMiscValue3,
            t.EffectSpellClassMask3_1, t.EffectSpellClassMask3_2, t.EffectSpellClassMask3_3);
        return mods;
    }

    private static void Add(ref List<SpellModifier>? mods, SpellTemplateData t,
        int effect, int aura, int basePoints, int miscValue, uint mask1, uint mask2, uint mask3)
    {
        if (effect != EffectApplyAura || aura is not (AuraAddFlatModifier or AuraAddPctModifier))
            return;
        if ((mask1 | mask2 | mask3) == 0)
            return;
        // CMaNGOS: величина эффекта = BasePoints + 1 (напр. Improved Heroic Strike: −11 → −10 ярости ×10).
        (mods ??= []).Add(new SpellModifier(t.Id, (SpellModOp)miscValue, aura == AuraAddPctModifier,
            basePoints + 1, t.SpellFamilyName, mask1, mask2, mask3));
    }

    /// <summary>Затрагивает ли модификатор целевой спелл: то же семейство + пересечение 96-битной маски
    /// (биты 0–63 — SpellFamilyFlags, 64–95 — SpellFamilyFlags2). CMaNGOS IsFitToFamilyMask.</summary>
    public static bool IsAffected(in SpellModifier mod, SpellCatalog.SpellInfo target)
        => mod.FamilyName == target.FamilyName && target.FamilyName != 0
           && ((mod.Mask1 & (uint)target.FamilyFlags)
               | (mod.Mask2 & (uint)(target.FamilyFlags >> 32))
               | (mod.Mask3 & target.FamilyFlags2)) != 0;

    /// <summary>
    /// Применяет модификаторы типа <paramref name="op"/> к базовой величине для целевого спелла:
    /// результат = (база + Σфлэт) × Πпроцентов — как CMaNGOS <c>Player::ApplySpellMod</c>.
    /// </summary>
    public static int Apply(IReadOnlyList<SpellModifier> mods, SpellCatalog.SpellInfo target,
        SpellModOp op, int baseValue)
    {
        var flat = 0;
        var mul = 1.0;
        for (var i = 0; i < mods.Count; i++)
        {
            var mod = mods[i];
            if (mod.Op != op || !IsAffected(mod, target))
                continue;
            if (mod.IsPct)
                mul *= (100.0 + mod.Value) / 100.0;
            else
                flat += mod.Value;
        }
        return (int)Math.Round((baseValue + flat) * mul);
    }

    /// <summary>
    /// Применяет модификаторы ВЕЛИЧИНЫ эффекта № <paramref name="effectIndex"/> (1..3): ALL_EFFECTS +
    /// EFFECT{N} (так Improved Rend (EFFECT1) растит тик Кровопускания, а Improved Cleave (ALL_EFFECTS) —
    /// бонус Рассекающего удара). Как CMaNGOS <c>Unit::CalculateSpellEffectValue</c>.
    /// </summary>
    public static int ApplyEffectValue(IReadOnlyList<SpellModifier> mods, SpellCatalog.SpellInfo target,
        int effectIndex, int baseValue)
    {
        var value = Apply(mods, target, SpellModOp.AllEffects, baseValue);
        var effectOp = effectIndex switch
        {
            1 => SpellModOp.Effect1,
            2 => SpellModOp.Effect2,
            3 => SpellModOp.Effect3,
            _ => (SpellModOp?)null,
        };
        return effectOp is { } eop ? Apply(mods, target, eop, value) : value;
    }
}
