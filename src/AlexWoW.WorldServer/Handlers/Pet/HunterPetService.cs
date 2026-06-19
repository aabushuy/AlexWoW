// Порт CMaNGOS-WoTLK: src/game/Entities/Pet.cpp (TameBeast + Happiness/Loyalty/Experience)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Entities/Pet.cpp. GPL-2.0.

using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Pet;

/// <summary>
/// Хантер-специфика (PET.T4): tame beast, happiness, exp share, pet level sync.
/// </summary>
internal sealed class HunterPetService(PetRegistry registry, PetPersistenceService persist,
    ILogger<HunterPetService> logger)
{
    /// <summary>Tame Beast (1515) — Hunter спецкаст.</summary>
    public const uint TameBeastSpellId = 1515;

    /// <summary>Max happiness CMaNGOS: 1050000 (1.05M, кратность от 5K тиков).</summary>
    public const uint MaxHappiness = 1050000;

    /// <summary>Стартовая happiness нового пета (середина шкалы — Content state).</summary>
    public const uint InitialHappiness = MaxHappiness / 2;

    /// <summary>Падение happiness на тик (1 час игры). CMaNGOS: −2500.</summary>
    private const uint HappinessDecayPerTick = 2500;

    /// <summary>Pet level = owner level − 5, минимум 1. Упрощённо относительно CMaNGOS.</summary>
    public static byte ComputePetLevel(byte ownerLevel) => (byte)System.Math.Max(1, ownerLevel - 5);

    /// <summary>
    /// Завершение Tame Beast (вызывается из SpellEffectsService после успеха каста 1515 на NPC):
    /// создаёт пета хантеру.
    /// </summary>
    public async Task<World.Pet?> CompleteTameAsync(WorldSession session, uint creatureEntry, string creatureName,
        byte targetLevel, CancellationToken ct)
    {
        var charGuid = (ulong)session.InWorldGuid;
        var existing = registry.GetByOwner(charGuid);
        if (existing is not null)
        {
            logger.LogWarning("TAME '{User}' уже имеет пета — отказ", session.Account);
            return null;
        }

        var ownerLevel = session.Character?.Level ?? 1;
        var petLevel = System.Math.Min(targetLevel, ComputePetLevel(ownerLevel));

        var pet = registry.Create(charGuid, creatureEntry, creatureName, petLevel);
        pet.Type = PetType.Hunter;
        pet.Happiness = InitialHappiness;
        await persist.SaveNewAsync(pet, ct);

        logger.LogInformation("TAME '{User}' приручил entry={Entry} как '{Name}' (lvl {L})",
            session.Account, creatureEntry, creatureName, petLevel);
        return pet;
    }

    /// <summary>Тик деградации happiness (каждый час игрового времени).</summary>
    public async Task DecayHappinessAsync(World.Pet pet, CancellationToken ct)
    {
        if (pet.Type != PetType.Hunter)
            return;
        var newH = pet.Happiness > HappinessDecayPerTick ? pet.Happiness - HappinessDecayPerTick : 0;
        if (newH == pet.Happiness)
            return;
        pet.Happiness = newH;
        await persist.UpdateAsync(pet, ct);
    }

    /// <summary>Кормление пета (целевым предметом-едой) → +happiness.</summary>
    public async Task FeedAsync(World.Pet pet, uint amount, CancellationToken ct)
    {
        if (pet.Type != PetType.Hunter)
            return;
        var newH = System.Math.Min(MaxHappiness, pet.Happiness + amount);
        if (newH == pet.Happiness)
            return;
        pet.Happiness = newH;
        await persist.UpdateAsync(pet, ct);
    }

    /// <summary>
    /// Pet level sync: при повышении уровня хозяина — поднять уровень пета (CMaNGOS Pet::GivePetLevel).
    /// </summary>
    public async Task SyncLevelToOwnerAsync(WorldSession session, byte newOwnerLevel, CancellationToken ct)
    {
        var pet = registry.GetByOwner((ulong)session.InWorldGuid);
        if (pet is null || pet.Type != PetType.Hunter)
            return;
        var targetPetLevel = ComputePetLevel(newOwnerLevel);
        if (targetPetLevel <= pet.Level)
            return;
        pet.Level = targetPetLevel;
        await persist.UpdateAsync(pet, ct);
    }
}
