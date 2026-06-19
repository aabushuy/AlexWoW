using AlexWoW.Database.Models;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Tests;

/// <summary>
/// Автотест маппинга <c>spell_template</c> → <see cref="SpellCatalog.SpellInfo"/> (M12.7, тир-1 Spell QA):
/// детерминированная проверка парсера на представительных строках по категориям эффектов. Ловит регрессии
/// разбора (школа, диапазон величины, стоимость/ресурс, периодика, движение, крафт) без БД и без игры.
/// </summary>
public class SpellCatalogFromTemplateTests
{
    [Fact]
    public void DirectSchoolDamage_RangeFromBasePointsAndDieSides()
    {
        // Fireball-подобный: BasePoints=13, DieSides=9 → 14..22, школа Fire, каст 1000мс.
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 1,
            SchoolMask = 4,
            CastingTimeIndex = 4,
            ManaCost = 30,
            PowerType = 0,
            Effect1 = 2,
            EffectBasePoints1 = 13,
            EffectDieSides1 = 9,
        });
        Assert.Equal(4, info.School);
        Assert.Equal(14, info.MinAmount);
        Assert.Equal(22, info.MaxAmount);
        Assert.Equal(1000, info.CastMs);
        Assert.Equal(30u, info.ManaCost);
        Assert.Equal(0, info.PowerType);
        Assert.False(info.IsHeal);
        Assert.False(info.WeaponDamage);
        Assert.False(info.Periodic);
        Assert.Equal(1, info.DirectEffectIndex);
    }

    [Fact]
    public void DirectHeal_IsHealWithRange()
    {
        // Lesser Heal-подобный: Effect=Heal(10), BasePoints=44, DieSides=12 → 45..56, школа Holy.
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 2,
            SchoolMask = 2,
            CastingTimeIndex = 16,
            ManaCost = 30,
            Effect1 = 10,
            EffectBasePoints1 = 44,
            EffectDieSides1 = 12,
        });
        Assert.True(info.IsHeal);
        Assert.Equal(45, info.MinAmount);
        Assert.Equal(56, info.MaxAmount);
        Assert.Equal(2, info.School);
        Assert.Equal(1500, info.CastMs);
    }

    [Fact]
    public void WeaponDamage_FlagAndRageCost()
    {
        // Heroic Strike-подобный: Effect=WeaponDamage(58), бонус 11, ресурс — ярость (PowerType=1), флэт-кост 15.
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 3,
            SchoolMask = 1,
            PowerType = 1,
            ManaCost = 15,
            Effect1 = 58,
            EffectBasePoints1 = 10,
            EffectDieSides1 = 1,
        });
        Assert.True(info.WeaponDamage);
        Assert.Equal(1, info.PowerType);
        Assert.Equal(15u, info.ManaCost);
        Assert.Equal(11, info.MinAmount);
        Assert.Equal(11, info.MaxAmount);
    }

    [Fact]
    public void WeaponPercentDamage_PercentFromBasePoints()
    {
        // Slam-подобный: Effect=WeaponPercentDamage(31), BasePoints=100 → урон = 100% оружия.
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 4,
            SchoolMask = 1,
            PowerType = 1,
            Effect1 = 31,
            EffectBasePoints1 = 100,
        });
        Assert.Equal(100u, info.WeaponPercent);
        Assert.True(info.WeaponDamage); // 31 относится к weapon-эффектам
    }

    [Fact]
    public void InstantWithCooldown()
    {
        // Fire Blast-подобный: мгновенный (CastingTimeIndex=1 → 0мс), кулдаун 8с (RecoveryTime).
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 5,
            SchoolMask = 4,
            CastingTimeIndex = 1,
            RecoveryTime = 8000,
            Effect1 = 2,
            EffectBasePoints1 = 23,
            EffectDieSides1 = 9,
        });
        Assert.Equal(0, info.CastMs);
        Assert.Equal(8000, info.CooldownMs);
    }

    [Fact]
    public void CategoryRecovery_TakenAsCooldownWhenLarger()
    {
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 6,
            Effect1 = 2,
            EffectBasePoints1 = 5,
            RecoveryTime = 1000,
            CategoryRecoveryTime = 9000,
        });
        Assert.Equal(9000, info.CooldownMs);
    }

    [Fact]
    public void PercentManaCost_OnlyForManaPower()
    {
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 7,
            PowerType = 0,
            ManaCostPercentage = 10,
            Effect1 = 2,
            EffectBasePoints1 = 5,
        });
        Assert.Equal(10u, info.ManaCostPct);
    }

    [Fact]
    public void EnergyPower_FlatCost()
    {
        // Разбойничья абилка: энергия (PowerType=3), флэт-кост 45, % маны игнорируется.
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 8,
            PowerType = 3,
            ManaCost = 45,
            ManaCostPercentage = 20,
            Effect1 = 58,
            EffectBasePoints1 = 5,
        });
        Assert.Equal(3, info.PowerType);
        Assert.Equal(45u, info.ManaCost);
        Assert.Equal(0u, info.ManaCostPct);
    }

    [Fact]
    public void Dot_PeriodicDamageTick()
    {
        // Corruption-подобный: ApplyAura(6) с PERIODIC_DAMAGE(3), тик = BasePoints+1, интервал 3с, длит. 60с.
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 9,
            SchoolMask = 32,
            DurationIndex = 3,
            Effect1 = 6,
            EffectApplyAuraName1 = 3,
            EffectBasePoints1 = 29,
            EffectAmplitude1 = 3000,
        });
        Assert.True(info.Periodic);
        Assert.False(info.PeriodicHeal);
        Assert.Equal(30, info.TickAmount);
        Assert.Equal(3000, info.TickIntervalMs);
        Assert.Equal(60000, info.AuraDurationMs);
        Assert.Equal(1, info.PeriodicEffectIndex);
    }

    [Fact]
    public void Hot_PeriodicHealTick()
    {
        // Renew-подобный: ApplyAura(6) с PERIODIC_HEAL(8), тик 45, длит. 15с.
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 10,
            SchoolMask = 2,
            DurationIndex = 8,
            Effect1 = 6,
            EffectApplyAuraName1 = 8,
            EffectBasePoints1 = 44,
            EffectAmplitude1 = 3000,
        });
        Assert.True(info.Periodic);
        Assert.True(info.PeriodicHeal);
        Assert.Equal(45, info.TickAmount);
    }

    [Fact]
    public void Buff_IncreaseHealth()
    {
        // Бафф +макс.HP: ApplyAura(6) MOD_INCREASE_HEALTH(34), BasePoints=79 → +80 HP, позитивная аура.
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 11,
            DurationIndex = 5,
            Effect1 = 6,
            EffectApplyAuraName1 = 34,
            EffectBasePoints1 = 79,
        });
        Assert.True(info.AuraBuff);
        Assert.True(info.AuraPositive);
        Assert.Equal(80, info.HealthBonus);
    }

    [Fact]
    public void Metamorphosis_ShapeshiftFormBuff()
    {
        // Метаморфоза ЧК (47241): APPLY_AURA(6) MOD_SHAPESHIFT(36), MiscValue=22 (форма демона), Bp=-1,
        // DurationIndex=9 (30с). Несмотря на отрицательный Bp ауры формы — это позитивный само-бафф с формой 22.
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 47241,
            DurationIndex = 9,
            Effect1 = 6,
            EffectApplyAuraName1 = 36,
            EffectMiscValue1 = 22,
            EffectBasePoints1 = -1,
        });
        Assert.True(info.AuraBuff);
        Assert.True(info.AuraPositive);
        Assert.Equal((byte)22, info.ShapeshiftForm);
        Assert.Equal(30000, info.AuraDurationMs);
    }

    [Fact]
    public void CurseOfTheElements_IsCurseWithDamageTakenAmp()
    {
        // §3 CoE (1490): MOD_RESISTANCE(22) Bp=-46 (дебафф) + MOD_DAMAGE_PERCENT_TAKEN(87) Bp=5 misc=126 (магия).
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 1490,
            DurationIndex = 5,
            Effect1 = 6,
            EffectApplyAuraName1 = 22,
            EffectBasePoints1 = -46,
            EffectMiscValue1 = 126,
            Effect2 = 6,
            EffectApplyAuraName2 = 87,
            EffectBasePoints2 = 5,
            EffectMiscValue2 = 126,
        });
        Assert.True(info.IsCurse);
        Assert.False(info.AuraPositive);              // дебафф на цель
        Assert.Equal(6, info.CurseDamageTakenPct);    // Bp+1 → +6% урона по проклятой цели
        Assert.Equal((byte)126, info.CurseSchoolMask);
    }

    [Fact]
    public void CurseOfWeakness_IsCurseWithoutAmp()
    {
        // §3 CoW (702): MOD_ATTACK_POWER(99) Bp=-22 — кёрс, но без амплификации урона (нет ауры 87).
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 702,
            DurationIndex = 4,
            Effect1 = 6,
            EffectApplyAuraName1 = 99,
            EffectBasePoints1 = -22,
        });
        Assert.True(info.IsCurse);
        Assert.Equal(0, info.CurseDamageTakenPct);
    }

    [Fact]
    public void NonCurseSpell_NotFlaggedAsCurse()
    {
        // Fireball (133) — не проклятие.
        var info = SpellCatalog.FromTemplate(new SpellTemplateData { Id = 133, Effect1 = 2, EffectBasePoints1 = 10 });
        Assert.False(info.IsCurse);
        Assert.Equal(0, info.CurseDamageTakenPct);
    }

    [Fact]
    public void Charge_MovementParsed()
    {
        var info = SpellCatalog.FromTemplate(new SpellTemplateData { Id = 12, Effect1 = 96 });
        Assert.Equal(SpellCatalog.SpellMovement.Charge, info.Movement);
    }

    [Fact]
    public void CreateItem_CraftParsed()
    {
        // CREATE_ITEM(24): результат 2841, count = BasePoints+1 = 2.
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 13,
            Effect1 = 24,
            EffectItemType1 = 2841,
            EffectBasePoints1 = 1,
        });
        Assert.Equal(2841u, info.CreateItemId);
        Assert.Equal(2u, info.CreateItemCount);
    }

    [Fact]
    public void SpellFamily_FlagsCarried()
    {
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 14,
            Effect1 = 2,
            EffectBasePoints1 = 5,
            SpellFamilyName = 3,
            SpellFamilyFlags = 0x1,
            SpellFamilyFlags2 = 0,
        });
        Assert.Equal(3u, info.FamilyName);
        Assert.Equal(0x1UL, info.FamilyFlags);
    }

    [Theory]
    // Брони мага (Frost/Ice/Mage/Molten) — одна эксклюзивная группа.
    [InlineData(168u, SpellCatalog.GroupMageArmor)]    // Frost Armor R1
    [InlineData(43008u, SpellCatalog.GroupMageArmor)]  // Ice Armor R6 (макс)
    [InlineData(43024u, SpellCatalog.GroupMageArmor)]  // Mage Armor R6 (макс)
    [InlineData(43046u, SpellCatalog.GroupMageArmor)]  // Molten Armor R3 (макс)
    // Брони чернокнижника (Demon Skin/Demon Armor/Fel Armor) — другая эксклюзивная группа.
    [InlineData(687u, SpellCatalog.GroupWarlockArmor)] // Demon Skin R1
    [InlineData(47889u, SpellCatalog.GroupWarlockArmor)] // Demon Armor R8 (макс)
    [InlineData(47893u, SpellCatalog.GroupWarlockArmor)] // Fel Armor R4 (макс)
    // Печати паладина — своя эксклюзивная группа.
    [InlineData(21084u, SpellCatalog.GroupPaladinSeal)]  // Seal of Righteousness
    [InlineData(20165u, SpellCatalog.GroupPaladinSeal)]  // Seal of Light
    [InlineData(20166u, SpellCatalog.GroupPaladinSeal)]  // Seal of Wisdom
    [InlineData(20164u, SpellCatalog.GroupPaladinSeal)]  // Seal of Justice
    public void ExclusiveArmor_MappedToGroup(uint spellId, byte expectedGroup)
        => Assert.Equal(expectedGroup, SpellCatalog.ExclusiveAuraGroup(spellId));

    [Theory]
    [InlineData(133u)]   // Fireball — обычный спелл, не эксклюзивная броня
    [InlineData(2457u)]  // стойка воина — это Toggle, а не ExclusiveAura
    public void NonArmor_NoExclusiveGroup(uint spellId)
        => Assert.Equal(0, SpellCatalog.ExclusiveAuraGroup(spellId));

    [Theory]
    // CC по типу аура-эффекта (SpellAuraDefines): 12=стан,26=рут,7=страх,27=немота,5=дезориентация.
    [InlineData(12, SpellCatalog.CrowdControlKind.Stun)]
    [InlineData(26, SpellCatalog.CrowdControlKind.Root)]
    [InlineData(7, SpellCatalog.CrowdControlKind.Fear)]
    [InlineData(27, SpellCatalog.CrowdControlKind.Silence)]
    [InlineData(5, SpellCatalog.CrowdControlKind.Disorient)]
    public void CrowdControl_DetectedByAuraType(int auraType, SpellCatalog.CrowdControlKind expected)
    {
        // APPLY_AURA(6) с CC-аурой + DurationIndex 27 (10 сек) → распознаётся как CC с длительностью.
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 100,
            DurationIndex = 27,
            Effect1 = 6,
            EffectApplyAuraName1 = auraType,
        });
        Assert.Equal(expected, info.CrowdControl);
        Assert.True(info.CrowdControlMs > 0);
    }

    [Fact]
    public void DamageDonePct_DetectedWithSchoolMask()
    {
        // Shadowform-подобный: APPLY_AURA(6) MOD_DAMAGE_PERCENT_DONE(79), MiscValue=32 (Shadow), Bp=14 → +15%.
        var info = SpellCatalog.FromTemplate(new SpellTemplateData
        {
            Id = 100,
            Effect2 = 6,
            EffectApplyAuraName2 = 79,
            EffectMiscValue2 = 32,
            EffectBasePoints2 = 14,
        });
        Assert.Equal(15, info.DamageDonePct);
        Assert.Equal(32, info.DamageDoneSchoolMask);
    }

    [Fact]
    public void CrowdControl_NoneForNonCcSpell()
    {
        var info = SpellCatalog.FromTemplate(new SpellTemplateData { Id = 1, Effect1 = 2, EffectBasePoints1 = 10 });
        Assert.Equal(SpellCatalog.CrowdControlKind.None, info.CrowdControl);
        Assert.Equal(0, info.CrowdControlMs);
    }

    [Theory]
    // Формы-шейпшифты — общая группа GroupShapeshift, форма из EffectMiscValue ауры 36.
    [InlineData(1784u, 30, SpellCatalog.GroupShapeshift)]  // Stealth R1
    [InlineData(1787u, 30, SpellCatalog.GroupShapeshift)]  // Stealth R4 (макс)
    [InlineData(15473u, 28, SpellCatalog.GroupShapeshift)] // Shadowform
    [InlineData(2645u, 16, SpellCatalog.GroupShapeshift)]  // Ghost Wolf
    // Присутствия DK — не шейпшифт (форма 0), своя эксклюзивная группа.
    [InlineData(48266u, 0, SpellCatalog.GroupDkPresence)]  // Blood Presence
    [InlineData(48263u, 0, SpellCatalog.GroupDkPresence)]  // Frost Presence
    [InlineData(48265u, 0, SpellCatalog.GroupDkPresence)]  // Unholy Presence
    public void FormToggle_MappedToFormAndGroup(uint spellId, byte expectedForm, byte expectedGroup)
    {
        Assert.True(SpellCatalog.TryGetToggle(spellId, out var toggle));
        Assert.Equal(expectedForm, toggle.Form);
        Assert.Equal(expectedGroup, toggle.Group);
    }
}
