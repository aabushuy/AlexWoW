using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Tests;

/// <summary>PET.T1+T5: каркас Pet + регистрация + persistence-rehydrate.</summary>
public sealed class PetTests
{
    [Fact]
    public void Create_assigns_owner_and_defaults()
    {
        var reg = new PetRegistry();
        var pet = reg.Create(ownerGuid: 0x10, entry: 416, name: "Imp", level: 1);

        Assert.Equal(0x10UL, pet.OwnerGuid);
        Assert.Equal(416u, pet.Entry);
        Assert.Equal("Imp", pet.Name);
        Assert.Equal(1, pet.Level);
        Assert.Equal(PetType.Summon, pet.Type);
        Assert.Equal(PetReactState.Defensive, pet.ReactState);
        Assert.Equal(PetCommandState.Follow, pet.CommandState);
        Assert.Same(pet, reg.GetByOwner(0x10));
    }

    [Fact]
    public void Rehydrate_sets_persisted_id_and_state()
    {
        var reg = new PetRegistry();
        var pet = reg.Rehydrate(persistedId: 5, ownerGuid: 0x20, entry: 416, name: "Old",
            level: 30, PetType.Hunter, PetReactState.Aggressive, PetCommandState.Stay,
            happiness: 1050000, exp: 1234);

        Assert.Equal(5u, pet.PersistedId);
        Assert.Equal(PetType.Hunter, pet.Type);
        Assert.Equal(PetReactState.Aggressive, pet.ReactState);
        Assert.Equal(PetCommandState.Stay, pet.CommandState);
        Assert.Equal(1050000u, pet.Happiness);
        Assert.Equal(1234u, pet.Experience);
    }

    [Fact]
    public void Remove_clears_owner_mapping()
    {
        var reg = new PetRegistry();
        reg.Create(0x10, 416, "Imp", 1);
        Assert.NotNull(reg.GetByOwner(0x10));
        reg.Remove(0x10);
        Assert.Null(reg.GetByOwner(0x10));
    }

    [Fact]
    public void All_enumerates_registered_pets()
    {
        var reg = new PetRegistry();
        reg.Create(0x10, 416, "Imp1", 1);
        reg.Create(0x20, 1860, "Vw", 10);
        Assert.Equal(2, reg.All.Count());
    }
}
