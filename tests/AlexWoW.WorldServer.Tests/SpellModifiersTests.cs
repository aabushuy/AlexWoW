using AlexWoW.Database.Models;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Tests;

/// <summary>
/// Математика модификаторов спеллов (M10.6): извлечение аур 107/108 из spell_template, матчинг по
/// семейству + 96-битной classmask, применение флэт/процент. Данные примеров — реальные строки
/// spell_template (дамп Spell.dbc 3.3.5a): Improved Heroic Strike 12282, Improved Rend 12286,
/// Improved Cleave 12329, Heroic Strike 78, Rend 772, Cleave 845.
/// </summary>
public sealed class SpellModifiersTests
{
    private const uint FamilyWarrior = 4;

    /// <summary>Heroic Strike 78: семейство воина, маска 64.</summary>
    private static SpellCatalog.SpellInfo HeroicStrike() => new(0, 11, 11, 0, ManaCost: 150, CooldownMs: 0,
        PowerType: 1, WeaponDamage: true, FamilyName: FamilyWarrior, FamilyFlags: 64, DirectEffectIndex: 1);

    /// <summary>Rend 772: семейство воина, маска 32, периодический эффект 1 (тик 5).</summary>
    private static SpellCatalog.SpellInfo Rend() => new(0, 0, 0, 0, ManaCost: 100, CooldownMs: 0,
        PowerType: 1, Periodic: true, TickAmount: 5, TickIntervalMs: 3000, AuraDurationMs: 9000,
        FamilyName: FamilyWarrior, FamilyFlags: 32, PeriodicEffectIndex: 1);

    /// <summary>Improved Heroic Strike ранг 1 (12282): аура 107, SPELLMOD_COST, −10 (ярость ×10), маска 64.</summary>
    private static SpellModifier ImpHeroicStrike() =>
        new(12282, SpellModOp.Cost, IsPct: false, Value: -10, FamilyWarrior, Mask1: 64, Mask2: 0, Mask3: 0);

    /// <summary>Improved Rend ранг 1 (12286): аура 108, SPELLMOD_EFFECT1, +10%, маска 32.</summary>
    private static SpellModifier ImpRend() =>
        new(12286, SpellModOp.Effect1, IsPct: true, Value: 10, FamilyWarrior, Mask1: 32, Mask2: 0, Mask3: 0);

    [Fact]
    public void Apply_FlatCost_ReducesRageCost()
    {
        // Improved Heroic Strike: 150 (15 ярости ×10) − 10 → 140 (14 ярости).
        var result = SpellModifiers.Apply([ImpHeroicStrike()], HeroicStrike(), SpellModOp.Cost, 150);
        Assert.Equal(140, result);
    }

    [Fact]
    public void Apply_DoesNotTouchUnmatchedSpell()
    {
        // Маска Improved Heroic Strike (64) не пересекается с Rend (32) — стоимость не меняется.
        var result = SpellModifiers.Apply([ImpHeroicStrike()], Rend(), SpellModOp.Cost, 100);
        Assert.Equal(100, result);
    }

    [Fact]
    public void Apply_DoesNotTouchOtherOp()
    {
        // Модификатор стоимости не влияет на запрос урона того же спелла.
        var result = SpellModifiers.Apply([ImpHeroicStrike()], HeroicStrike(), SpellModOp.Damage, 50);
        Assert.Equal(50, result);
    }

    [Fact]
    public void ApplyEffectValue_PctIncreasesRendTick()
    {
        // Improved Rend +10%: тик 5 → 6 (округление 5.5 вверх).
        var result = SpellModifiers.ApplyEffectValue([ImpRend()], Rend(), effectIndex: 1, baseValue: 5);
        Assert.Equal(6, result);
    }

    [Fact]
    public void ApplyEffectValue_EffectOpDoesNotLeakToOtherIndex()
    {
        // SPELLMOD_EFFECT1 не трогает величину эффекта 2 того же спелла.
        var result = SpellModifiers.ApplyEffectValue([ImpRend()], Rend(), effectIndex: 2, baseValue: 5);
        Assert.Equal(5, result);
    }

    [Fact]
    public void Apply_FlatAndPct_CombineLikeCmangos()
    {
        // (база + Σфлэт) × Πпроцентов: (100 + 20) × 1.5 = 180.
        var flat = new SpellModifier(1, SpellModOp.Damage, IsPct: false, Value: 20, FamilyWarrior, 64, 0, 0);
        var pct = new SpellModifier(2, SpellModOp.Damage, IsPct: true, Value: 50, FamilyWarrior, 64, 0, 0);
        var result = SpellModifiers.Apply([flat, pct], HeroicStrike(), SpellModOp.Damage, 100);
        Assert.Equal(180, result);
    }

    [Fact]
    public void IsAffected_MatchesHighMaskWords()
    {
        // Маска во 2-м/3-м слове (биты 32–63 и 64–95) тоже матчится.
        var target = HeroicStrike() with { FamilyFlags = 1UL << 40, FamilyFlags2 = 0x10 };
        var byMask2 = new SpellModifier(1, SpellModOp.Damage, false, 1, FamilyWarrior, 0, 1u << 8, 0);
        var byMask3 = new SpellModifier(2, SpellModOp.Damage, false, 1, FamilyWarrior, 0, 0, 0x10);
        Assert.True(SpellModifiers.IsAffected(ImpHeroicStrike() with { Mask1 = 0, Mask2 = 1u << 8 }, target));
        Assert.True(SpellModifiers.IsAffected(byMask2, target));
        Assert.True(SpellModifiers.IsAffected(byMask3, target));
    }

    [Fact]
    public void IsAffected_RejectsOtherFamilyAndZeroFamily()
    {
        var mageSpell = HeroicStrike() with { FamilyName = 3 };       // семейство мага
        var noFamily = HeroicStrike() with { FamilyName = 0 };        // вне семейств (NPC-спеллы)
        Assert.False(SpellModifiers.IsAffected(ImpHeroicStrike(), mageSpell));
        Assert.False(SpellModifiers.IsAffected(ImpHeroicStrike(), noFamily));
    }

    [Fact]
    public void ExtractFrom_ReadsFlatCostModifier()
    {
        // Improved Heroic Strike 12282: Effect1=6, Aura=107, BasePoints=−11 → value −10, MiscValue=14 (COST).
        var tpl = new SpellTemplateData
        {
            Id = 12282,
            SpellFamilyName = FamilyWarrior,
            Effect1 = 6,
            EffectApplyAuraName1 = 107,
            EffectBasePoints1 = -11,
            EffectMiscValue1 = 14,
            EffectSpellClassMask1_1 = 64,
        };
        var mods = SpellModifiers.ExtractFrom(tpl);
        var mod = Assert.Single(mods!);
        Assert.Equal(SpellModOp.Cost, mod.Op);
        Assert.False(mod.IsPct);
        Assert.Equal(-10, mod.Value);
        Assert.Equal(64u, mod.Mask1);
    }

    [Fact]
    public void ExtractFrom_ReadsPctModifier_AndIgnoresOrdinarySpells()
    {
        // Improved Rend 12286: Aura=108, BasePoints=9 → +10%, MiscValue=3 (EFFECT1).
        var imp = new SpellTemplateData
        {
            Id = 12286,
            SpellFamilyName = FamilyWarrior,
            Effect1 = 6,
            EffectApplyAuraName1 = 108,
            EffectBasePoints1 = 9,
            EffectMiscValue1 = 3,
            EffectSpellClassMask1_1 = 32,
        };
        var mod = Assert.Single(SpellModifiers.ExtractFrom(imp)!);
        Assert.Equal(SpellModOp.Effect1, mod.Op);
        Assert.True(mod.IsPct);
        Assert.Equal(10, mod.Value);

        // Обычный спелл (Rend 772: аура PERIODIC_DAMAGE) — не модификатор.
        var rend = new SpellTemplateData { Id = 772, Effect1 = 6, EffectApplyAuraName1 = 3, EffectBasePoints1 = 4 };
        Assert.Null(SpellModifiers.ExtractFrom(rend));

        // Аура 107 с пустой classmask — матчить нечего, пропускается.
        var empty = new SpellTemplateData { Id = 1, Effect1 = 6, EffectApplyAuraName1 = 107, EffectMiscValue1 = 14 };
        Assert.Null(SpellModifiers.ExtractFrom(empty));
    }
}
