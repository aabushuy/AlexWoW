using AlexWoW.WorldServer.Handlers.Pet;

namespace AlexWoW.WorldServer.Tests;

/// <summary>PET.T4: Hunter pet формулы (level/happiness).</summary>
public sealed class HunterPetTests
{
    [Theory]
    [InlineData(60, 55)]
    [InlineData(10, 5)]
    [InlineData(5, 1)]   // owner ниже 6 → min 1
    [InlineData(1, 1)]
    public void Pet_level_is_owner_minus_5_floor_1(byte ownerLevel, byte expectedPetLevel)
    {
        Assert.Equal(expectedPetLevel, HunterPetService.ComputePetLevel(ownerLevel));
    }

    [Fact]
    public void Max_happiness_constant_matches_cmangos()
    {
        Assert.Equal(1050000u, HunterPetService.MaxHappiness);
    }

    [Fact]
    public void Initial_happiness_is_mid_range_content()
    {
        Assert.Equal(525000u, HunterPetService.InitialHappiness);
    }
}
