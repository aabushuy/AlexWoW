using AlexWoW.Database.Models;
using AlexWoW.Web.Services;

namespace AlexWoW.Web.Tests;

/// <summary>Справочники предметов для UI (без БД): качество/слот/тип/классы/DPS.</summary>
public sealed class ItemDisplayTests
{
    [Theory]
    [InlineData(1u, "#ffffff")] // Обычное — белый
    [InlineData(4u, "#a335ee")] // Эпическое — фиолетовый
    public void QualityColor_known_values(uint quality, string expected)
        => Assert.Equal(expected, ItemDisplay.QualityColor(quality));

    [Fact]
    public void QualityColor_unknown_falls_back_to_common()
        => Assert.Equal("#ffffff", ItemDisplay.QualityColor(99));

    [Theory]
    [InlineData(7u, "Ноги")]
    [InlineData(1u, "Голова")]
    public void SlotName_maps_inventory_type(uint invType, string expected)
        => Assert.Equal(expected, ItemDisplay.SlotName(invType));

    [Fact]
    public void SlotName_null_for_non_equip()
        => Assert.Null(ItemDisplay.SlotName(0));

    [Theory]
    [InlineData(2u, 7u, "Оружие — Меч (одноруч.)")]
    [InlineData(4u, 1u, "Доспех — Ткань")]
    [InlineData(4u, 6u, "Доспех — Щит")]
    public void TypeName_class_subclass(uint cls, uint sub, string expected)
        => Assert.Equal(expected, ItemDisplay.TypeName(cls, sub));

    [Fact]
    public void AllowableClasses_minus_one_is_all()
        => Assert.Null(ItemDisplay.AllowableClasses(-1));

    [Fact]
    public void AllowableClasses_decodes_bitmask()
    {
        // Биты воина (1<<0) и разбойника (1<<3) = 1 + 8 = 9.
        var classes = ItemDisplay.AllowableClasses(9);
        Assert.NotNull(classes);
        Assert.Contains("Воин", classes!);
        Assert.Contains("Разбойник", classes!);
        Assert.Equal(2, classes!.Count);
    }

    [Fact]
    public void Dps_computes_from_damage_and_delay()
    {
        var item = new ItemTemplateData
        {
            Delay = 2000, // 2.0 сек
            Damages = [new ItemDamage(10f, 30f, 0), new ItemDamage(0f, 0f, 0)],
        };
        // (10+30)/2 / 2.0 = 10 DPS
        Assert.Equal(10f, ItemDisplay.Dps(item), 3);
    }

    [Fact]
    public void Dps_zero_when_no_delay()
        => Assert.Equal(0f, ItemDisplay.Dps(new ItemTemplateData { Delay = 0 }));
}
