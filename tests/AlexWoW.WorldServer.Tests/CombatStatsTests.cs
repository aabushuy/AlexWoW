using AlexWoW.DataStores;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Tests;

/// <summary>Вторичные защитные статы (срез M-защита): броня/парри/блок + крит/уклонение из gt-таблиц.</summary>
public sealed class CombatStatsTests
{
    [Fact]
    public void Armor_is_two_per_agility_plus_items()
    {
        Assert.Equal(0u, CombatStats.Armor(0, 0));
        Assert.Equal(200u, CombatStats.Armor(100, 0));
        Assert.Equal(350u, CombatStats.Armor(100, 150));
    }

    [Theory]
    [InlineData(1, true, 5f)]    // воин с оружием
    [InlineData(1, false, 0f)]   // воин без оружия — парри нет
    [InlineData(8, true, 0f)]    // маг не парирует
    public void Parry_needs_weapon_and_capable_class(byte cls, bool weapon, float expected)
        => Assert.Equal(expected, CombatStats.ParryPercent(cls, weapon));

    [Theory]
    [InlineData(1, true, 5f)]    // воин со щитом
    [InlineData(1, false, 0f)]   // воин без щита — блока нет
    [InlineData(3, true, 0f)]    // охотник щит не носит → блока нет
    public void Block_needs_shield_and_capable_class(byte cls, bool shield, float expected)
        => Assert.Equal(expected, CombatStats.BlockPercent(cls, shield));

    [Fact]
    public void DefenseSkill_is_five_per_level()
    {
        Assert.Equal((ushort)400, CombatStats.DefenseSkill(80));
        Assert.Equal((ushort)5, CombatStats.DefenseSkill(1));
    }

    // --- gt-таблицы боевых рейтингов ---

    [Fact]
    public void Ratings_load_from_embedded_table()
        => Assert.True(new CombatRatings().Available, "combat_ratings.json не загрузился");

    [Fact]
    public void MeleeCrit_at_zero_agility_equals_class_base()
    {
        // Воин: critBase[0] = 0.031891 → 3.1891% при нулевой ловкости.
        var crit = new CombatRatings().MeleeCritPercent(cls: 1, level: 80, agi: 0);
        Assert.InRange(crit, 3.18f, 3.20f);
    }

    [Fact]
    public void Dodge_at_zero_agility_equals_class_base()
    {
        // Воин: PLAYER_BASE_DODGE[1] = 3.664% при нулевой ловкости.
        var dodge = new CombatRatings().DodgePercent(cls: 1, level: 80, agi: 0);
        Assert.InRange(dodge, 3.66f, 3.67f);
    }

    [Fact]
    public void Crit_and_dodge_grow_with_agility()
    {
        var r = new CombatRatings();
        Assert.True(r.MeleeCritPercent(1, 80, 1000) > r.MeleeCritPercent(1, 80, 0));
        Assert.True(r.DodgePercent(1, 80, 1000) > r.DodgePercent(1, 80, 0));
    }

    [Fact]
    public void Unknown_class_is_safe()
    {
        var r = new CombatRatings();
        Assert.Equal(0f, r.MeleeCritPercent(cls: 0, level: 80, agi: 100));
        Assert.Equal(0f, r.DodgePercent(cls: 99, level: 80, agi: 100));
    }
}
