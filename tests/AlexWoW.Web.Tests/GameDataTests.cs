using AlexWoW.Web.Services;

namespace AlexWoW.Web.Tests;

/// <summary>Справочники и правила рас/классов для админ-правки и M8.6 (без БД).</summary>
public sealed class GameDataTests
{
    [Theory]
    [InlineData(11, 4, true)]   // Друид — Ночной эльф
    [InlineData(11, 6, true)]   // Друид — Таурен
    [InlineData(11, 1, false)]  // Друид — Человек (нельзя)
    [InlineData(2, 1, true)]    // Паладин — Человек
    [InlineData(2, 10, true)]   // Паладин — Эльф крови
    [InlineData(2, 2, false)]   // Паладин — Орк (нельзя)
    public void RaceAllowedForClass_matches_wotlk_matrix(byte cls, byte race, bool expected)
        => Assert.Equal(expected, GameData.RaceAllowedForClass(race, cls));

    [Fact]
    public void DeathKnight_allows_every_known_race()
    {
        foreach (var r in GameData.AllRaces)
            Assert.True(GameData.RaceAllowedForClass(r.Key, cls: 6));
    }

    [Theory]
    [InlineData(1, true)]    // Человек
    [InlineData(10, true)]   // Эльф крови
    [InlineData(9, false)]   // нет расы с id 9
    [InlineData(0, false)]
    public void RaceExists_only_for_known_ids(byte race, bool expected)
        => Assert.Equal(expected, GameData.RaceExists(race));

    [Fact]
    public void RacesForClassSameFaction_keeps_faction_and_class()
    {
        // Воин-Человек (Альянс): только альянсовые расы, допустимые для воина.
        var races = GameData.RacesForClassSameFaction(cls: 1, currentRace: 1);
        Assert.Contains(races, r => r.Key == 1);   // Человек
        Assert.Contains(races, r => r.Key == 3);   // Дворф
        Assert.DoesNotContain(races, r => r.Key == 2);  // Орк (Орда) — нет
        Assert.All(races, r => Assert.True(GameData.RaceAllowedForClass(r.Key, 1)));
    }

    [Fact]
    public void SplitMoney_decomposes_copper()
    {
        var (gold, silver, copper) = GameData.SplitMoney(123456);
        Assert.Equal(12u, gold);
        Assert.Equal(34u, silver);
        Assert.Equal(56u, copper);
    }

    [Fact]
    public void AllGenders_has_two_entries()
        => Assert.Equal(2, GameData.AllGenders.Count);
}
