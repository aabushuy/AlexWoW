using AlexWoW.Database.Analysis;
using AlexWoW.Database.Models;

namespace AlexWoW.WorldServer.Tests;

/// <summary>Тесты анализа аномалий захвата проверки заклинаний (M12 Spell QA, чистая логика).</summary>
public class SpellTestAnalyzerTests
{
    private static SpellTestResult Result(
        uint spellId = 100,
        SpellTestResultType type = SpellTestResultType.DirectDamage,
        uint amount = 50,
        uint effective = 50,
        uint expectedMin = 40,
        uint expectedMax = 60,
        byte school = 4,
        bool isHeal = false,
        bool weaponBased = false)
        => new()
        {
            SpellId = spellId,
            ResultType = type,
            Amount = amount,
            Effective = effective,
            ExpectedMin = expectedMin,
            ExpectedMax = expectedMax,
            School = school,
            IsHeal = isHeal,
            WeaponBased = weaponBased,
        };

    [Fact]
    public void InRange_NoAnomalies()
    {
        var analysis = SpellTestAnalyzer.Analyze([Result(amount: 50)]);
        Assert.False(analysis.HasAnomalies);
        Assert.Equal(1, analysis.TotalResults);
        Assert.Equal(1, analysis.DistinctSpells);
    }

    [Fact]
    public void BelowExpected_Flagged()
    {
        var analysis = SpellTestAnalyzer.Analyze([Result(amount: 10, expectedMin: 40, expectedMax: 60)]);
        Assert.Contains(analysis.Anomalies, a => a.Kind == SpellAnomalyKind.BelowExpected);
    }

    [Fact]
    public void AboveExpected_Flagged()
    {
        var analysis = SpellTestAnalyzer.Analyze([Result(amount: 999, expectedMin: 40, expectedMax: 60)]);
        Assert.Contains(analysis.Anomalies, a => a.Kind == SpellAnomalyKind.AboveExpected);
    }

    [Fact]
    public void WeaponBased_AboveMax_NotFlagged()
    {
        // weapon-абилка: бросок оружия закономерно превышает [min;max] — «выше максимума» не флагуем.
        var analysis = SpellTestAnalyzer.Analyze([Result(amount: 999, expectedMin: 40, expectedMax: 60, weaponBased: true)]);
        Assert.DoesNotContain(analysis.Anomalies, a => a.Kind is SpellAnomalyKind.AboveExpected or SpellAnomalyKind.BelowExpected);
    }

    [Fact]
    public void WeaponBased_BelowMin_Flagged()
    {
        // weapon-абилка: оружие лишь ДОБАВЛЯЕТ поверх flat-бонуса, поэтому величина ниже min =
        // непрокинутый/заниженный flat-компонент (реальный баг) — флагуем даже для weapon.
        var analysis = SpellTestAnalyzer.Analyze([Result(amount: 10, expectedMin: 40, expectedMax: 60, weaponBased: true)]);
        Assert.Contains(analysis.Anomalies, a => a.Kind == SpellAnomalyKind.BelowExpected);
    }

    [Fact]
    public void ZeroDamage_Flagged()
    {
        var analysis = SpellTestAnalyzer.Analyze([Result(amount: 0, expectedMin: 0, expectedMax: 0)]);
        Assert.Contains(analysis.Anomalies, a => a.Kind == SpellAnomalyKind.ZeroDamage);
    }

    [Fact]
    public void ZeroHeal_Flagged()
    {
        var r = Result(type: SpellTestResultType.DirectHeal, amount: 0, effective: 0,
            expectedMin: 0, expectedMax: 0, school: 2, isHeal: true);
        var analysis = SpellTestAnalyzer.Analyze([r]);
        Assert.Contains(analysis.Anomalies, a => a.Kind == SpellAnomalyKind.ZeroHeal);
    }

    [Fact]
    public void Overheal_EffectiveZero_NotFlagged()
    {
        // Цель была полна (effective==0), но вычисленный хил >0 — это овёрхил, не баг спелла.
        var r = Result(type: SpellTestResultType.DirectHeal, amount: 50, effective: 0,
            expectedMin: 40, expectedMax: 60, school: 2, isHeal: true);
        var analysis = SpellTestAnalyzer.Analyze([r]);
        Assert.False(analysis.HasAnomalies);
    }

    [Fact]
    public void MissingSchool_OnDamage_Flagged()
    {
        var analysis = SpellTestAnalyzer.Analyze([Result(amount: 50, school: 0)]);
        Assert.Contains(analysis.Anomalies, a => a.Kind == SpellAnomalyKind.MissingSchool);
    }

    [Fact]
    public void Dedup_PerSpellAndKind()
    {
        // Несколько кастов одного спелла с одной аномалией → одна запись.
        var rows = new[]
        {
            Result(amount: 10, expectedMin: 40, expectedMax: 60),
            Result(amount: 11, expectedMin: 40, expectedMax: 60),
            Result(amount: 12, expectedMin: 40, expectedMax: 60),
        };
        var analysis = SpellTestAnalyzer.Analyze(rows);
        Assert.Single(analysis.Anomalies);
        Assert.Equal(3, analysis.TotalResults);
    }
}
