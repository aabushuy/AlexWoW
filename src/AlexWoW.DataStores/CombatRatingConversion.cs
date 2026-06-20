namespace AlexWoW.DataStores;

/// <summary>
/// Конверсия combat ratings (3.3.5a) → проценты (SPELL.T1). EffectMiscValue ауры
/// MOD_RATING (189) — это битовая маска <see cref="CombatRating"/>; <c>BasePoints+1</c> — величина рейтинга
/// в очках. На уровне 80 (cap) делитель «очков на 1%» зашит ниже (эталон CMaNGOS gtCombatRatings.dbc,
/// WotLK pre-Cata). Для более низких уровней — линейная аппроксимация (rating ниже скейлится мягче).
/// </summary>
public static class CombatRatingConversion
{
    /// <summary>Биты EffectMiscValue ауры 189 MOD_RATING. Совпадают с <c>enum CombatRating</c> CMaNGOS
    /// (Unit.h) и значениями PLAYER_FIELD_COMBAT_RATING_1..25 (3.3.5a). 1 &lt;&lt; (значение) = битмаска.</summary>
    public enum CombatRating : byte
    {
        WeaponSkill = 0,
        DefenseSkill = 1,
        Dodge = 2,
        Parry = 3,
        Block = 4,
        HitMelee = 5,
        HitRanged = 6,
        HitSpell = 7,
        CritMelee = 8,
        CritRanged = 9,
        CritSpell = 10,
        HitTakenMelee = 11,
        HitTakenRanged = 12,
        HitTakenSpell = 13,
        CritTakenMelee = 14,
        CritTakenRanged = 15,
        CritTakenSpell = 16,
        HasteMelee = 17,
        HasteRanged = 18,
        HasteSpell = 19,
        WeaponSkillMainhand = 20,
        WeaponSkillOffhand = 21,
        WeaponSkillRanged = 22,
        Expertise = 23,
        ArmorPenetration = 24,
    }

    /// <summary>Очки рейтинга на 1% при уровне 80 (cap, WotLK). 0 — рейтинг не процентный (weapon skill,
    /// defense — отдельные единицы; для них пока возвращаем 0% — отдельная задача).</summary>
    private static readonly float[] RatingPerPctAt80 =
    {
        0f,      // 0 WeaponSkill        — в очках навыка, не %
        0f,      // 1 DefenseSkill       — в очках навыка, не %
        45.25f,  // 2 Dodge
        45.25f,  // 3 Parry
        16.39f,  // 4 Block
        32.79f,  // 5 HitMelee
        32.79f,  // 6 HitRanged
        26.232f, // 7 HitSpell
        45.91f,  // 8 CritMelee
        45.91f,  // 9 CritRanged
        45.91f,  // 10 CritSpell
        32.79f,  // 11 HitTakenMelee
        32.79f,  // 12 HitTakenRanged
        26.232f, // 13 HitTakenSpell
        45.91f,  // 14 CritTakenMelee
        45.91f,  // 15 CritTakenRanged
        45.91f,  // 16 CritTakenSpell
        32.79f,  // 17 HasteMelee
        32.79f,  // 18 HasteRanged
        32.79f,  // 19 HasteSpell
        0f, 0f, 0f, // 20-22 weapon skill специализаций
        32.79f,  // 23 Expertise (1 expertise = 1 ≈ 32.79 на 80; в expertise rating)
        4.92f,   // 24 ArmorPenetration (~)
    };

    /// <summary>Делитель «очков на 1%» при уровне 80; 0 — рейтинг не процентный.
    /// Для уровней &lt; 80 шкала упрощённо линейная (rating даёт больше %). На level 70 — ×0.875, на 60 — ×0.75.</summary>
    private static float DivisorFor(CombatRating type, byte level)
    {
        var idx = (int)type;
        if (idx < 0 || idx >= RatingPerPctAt80.Length)
            return 0f;
        var div80 = RatingPerPctAt80[idx];
        if (div80 == 0f)
            return 0f;
        if (level >= 80)
            return div80;
        // Прямая аппроксимация WoW pre-Cata: рейтинг «дешевеет» на 1.5% на каждый уровень ниже 70 в диапазоне
        // 60..70, на 1.75% на каждый уровень в диапазоне 70..80 (cap 80). Приближение, расхождение в пределах %.
        var lvl = (int)level;
        float scale = lvl switch
        {
            >= 70 => 1f - (80 - lvl) * 0.0175f,
            _ => 0.825f - Math.Max(0, 70 - lvl) * 0.015f,
        };
        if (scale < 0.4f)
            scale = 0.4f;
        return div80 * scale;
    }

    /// <summary>Конвертирует <paramref name="rating"/> заданного типа в проценты при <paramref name="level"/>;
    /// 0% для неконвертируемых типов (weapon skill / defense skill).</summary>
    public static float ToPct(CombatRating type, int rating, byte level)
    {
        var div = DivisorFor(type, level);
        return div > 0 ? rating / div : 0f;
    }

    /// <summary>Аккумулятор: рейтинги (плоско, по типу) → итоговые проценты для combat-резолверов.
    /// Применяется к MOD_RATING (189): один эффект может затрагивать несколько рейтингов (битмаска).</summary>
    public struct RatingPercents
    {
        public float MeleeHitPct;
        public float RangedHitPct;
        public float SpellHitPct;
        public float MeleeCritPct;
        public float RangedCritPct;
        public float SpellCritPct;
        public float DodgePct;
        public float ParryPct;
        public float BlockPct;
        public float MeleeHastePct;
        public float RangedHastePct;
        public float SpellHastePct;
    }

    /// <summary>Раскладывает MOD_RATING (битмаска <paramref name="ratingMask"/>, очки <paramref name="rating"/>,
    /// уровень <paramref name="level"/>) в проценты по типам — каждый бит = тот же rating-value.</summary>
    public static RatingPercents Distribute(uint ratingMask, int rating, byte level)
    {
        var r = new RatingPercents();
        for (var i = 0; i <= (int)CombatRating.ArmorPenetration; i++)
        {
            if ((ratingMask & (1u << i)) == 0)
                continue;
            var pct = ToPct((CombatRating)i, rating, level);
            switch ((CombatRating)i)
            {
                case CombatRating.HitMelee: r.MeleeHitPct += pct; break;
                case CombatRating.HitRanged: r.RangedHitPct += pct; break;
                case CombatRating.HitSpell: r.SpellHitPct += pct; break;
                case CombatRating.CritMelee: r.MeleeCritPct += pct; break;
                case CombatRating.CritRanged: r.RangedCritPct += pct; break;
                case CombatRating.CritSpell: r.SpellCritPct += pct; break;
                case CombatRating.Dodge: r.DodgePct += pct; break;
                case CombatRating.Parry: r.ParryPct += pct; break;
                case CombatRating.Block: r.BlockPct += pct; break;
                case CombatRating.HasteMelee: r.MeleeHastePct += pct; break;
                case CombatRating.HasteRanged: r.RangedHastePct += pct; break;
                case CombatRating.HasteSpell: r.SpellHastePct += pct; break;
                // taken/expertise/weapon skill пока игнорируем — отдельная задача T1+
            }
        }
        return r;
    }
}
