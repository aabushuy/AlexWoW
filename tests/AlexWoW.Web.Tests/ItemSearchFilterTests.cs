using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Web.Tests;

/// <summary>Сборка WHERE для поиска предметов (без БД): условия и параметры по заполненным полям.</summary>
public sealed class ItemSearchFilterTests
{
    [Fact]
    public void Empty_filter_yields_match_all()
    {
        var p = new DynamicParameters();
        var where = new ItemSearchFilter().BuildWhere(p);
        Assert.Equal("1=1", where);
        Assert.Empty(p.ParameterNames);
    }

    [Fact]
    public void Level_range_adds_both_bounds()
    {
        var p = new DynamicParameters();
        var where = new ItemSearchFilter { LevelMin = 10, LevelMax = 60 }.BuildWhere(p);
        Assert.Contains("RequiredLevel >= @lvlMin", where);
        Assert.Contains("RequiredLevel <= @lvlMax", where);
        Assert.Equal(10u, p.Get<uint>("lvlMin"));
        Assert.Equal(60u, p.Get<uint>("lvlMax"));
    }

    [Theory]
    [InlineData(ItemKind.Weapon, 2u)]
    [InlineData(ItemKind.Armor, 4u)]
    public void Kind_maps_to_item_class(ItemKind kind, uint expectedClass)
    {
        var p = new DynamicParameters();
        var where = new ItemSearchFilter { Kind = kind }.BuildWhere(p);
        Assert.Contains("class = @class", where);
        Assert.Equal(expectedClass, p.Get<uint>("class"));
    }

    [Fact]
    public void Player_class_uses_allowable_bitmask()
    {
        // Разбойник = класс 4 → бит = 1 << 3 = 8.
        var p = new DynamicParameters();
        var where = new ItemSearchFilter { PlayerClass = 4 }.BuildWhere(p);
        Assert.Contains("AllowableClass = -1 OR (AllowableClass & @classMask)", where);
        Assert.Equal(8, p.Get<int>("classMask"));
    }

    [Fact]
    public void Invalid_player_class_is_ignored()
    {
        var p = new DynamicParameters();
        var where = new ItemSearchFilter { PlayerClass = 99 }.BuildWhere(p);
        Assert.Equal("1=1", where);
    }

    [Fact]
    public void Name_spaces_become_wildcards()
    {
        var p = new DynamicParameters();
        var where = new ItemSearchFilter { NameContains = "Gladiator Plate" }.BuildWhere(p);
        Assert.Contains("name LIKE @name", where);
        Assert.Equal("%Gladiator%Plate%", p.Get<string>("name"));
    }

    [Fact]
    public void Multiple_filters_joined_with_and()
    {
        var p = new DynamicParameters();
        var where = new ItemSearchFilter { LevelMin = 1, Kind = ItemKind.Armor, NameContains = "x" }.BuildWhere(p);
        Assert.Equal(3, where.Split(" AND ").Length);
    }
}
