// Порт CMaNGOS-WoTLK: src/game/Entities/Pet.cpp (SavePetToDB / LoadPetFromDB)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Entities/Pet.cpp. GPL-2.0.

using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;
using EfPet = AlexWoW.Database.Entities.CharacterPet;

namespace AlexWoW.WorldServer.Handlers.Pet;

/// <summary>Persistence пета (PET.T5).</summary>
internal sealed class PetPersistenceService(IPetRepository repo, ILogger<PetPersistenceService> logger)
{
    public async Task SaveNewAsync(World.Pet pet, CancellationToken ct)
    {
        try
        {
            var newId = await repo.InsertAsync(ToEf(pet), ct);
            pet.PersistedId = newId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PET persistence: SaveNew failed: {Msg}", ex.Message);
        }
    }

    public async Task UpdateAsync(World.Pet pet, CancellationToken ct)
    {
        if (pet.PersistedId == 0)
            return;
        try { await repo.UpdateAsync(ToEf(pet), ct); }
        catch (Exception ex) { logger.LogWarning(ex, "PET persistence: Update id={Id} failed", pet.PersistedId); }
    }

    public async Task DeleteAsync(World.Pet pet, CancellationToken ct)
    {
        if (pet.PersistedId == 0)
            return;
        try { await repo.DeleteAsync(pet.PersistedId, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "PET persistence: Delete id={Id} failed", pet.PersistedId); }
    }

    private static EfPet ToEf(World.Pet p) => new()
    {
        Id = p.PersistedId,
        OwnerGuid = (uint)p.OwnerGuid,
        Entry = p.Entry,
        Name = p.Name,
        Level = p.Level,
        Experience = p.Experience,
        Health = p.Health,
        MaxHealth = p.MaxHealth,
        Mana = p.Mana,
        MaxMana = p.MaxMana,
        Type = (byte)p.Type,
        ReactState = (byte)p.ReactState,
        CommandState = (byte)p.CommandState,
        Happiness = p.Happiness,
        SummonedAt = p.SummonedAt,
    };
}
