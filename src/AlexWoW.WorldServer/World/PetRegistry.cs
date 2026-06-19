// Порт CMaNGOS-WoTLK: src/game/Entities/Pet.cpp (sObjectMgr.AddPet/GetPet)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Entities/Pet.cpp. GPL-2.0.

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Реестр петов в мире: char_guid → Pet. Один игрок — один активный пет
/// (CMaNGOS Player::GetPet). Минипеты-компаньоны — отдельная категория (T4).
/// </summary>
internal sealed class PetRegistry
{
    private readonly Dictionary<ulong, Pet> _byOwner = [];
    private uint _nextId = 1;

    public Pet? GetByOwner(ulong ownerGuid) => _byOwner.GetValueOrDefault(ownerGuid);

    public Pet Create(ulong ownerGuid, uint entry, string name, byte level)
    {
        var p = new Pet
        {
            Id = _nextId++,
            OwnerGuid = ownerGuid,
            Entry = entry,
            Name = name,
            Level = level,
        };
        _byOwner[ownerGuid] = p;
        return p;
    }

    /// <summary>T5: восстановить из БД (HostedService).</summary>
    public Pet Rehydrate(uint persistedId, ulong ownerGuid, uint entry, string name, byte level,
        PetType type, PetReactState react, PetCommandState command, uint happiness, uint exp)
    {
        var p = new Pet
        {
            Id = _nextId++,
            PersistedId = persistedId,
            OwnerGuid = ownerGuid,
            Entry = entry,
            Name = name,
            Level = level,
            Type = type,
            ReactState = react,
            CommandState = command,
            Happiness = happiness,
            Experience = exp,
        };
        _byOwner[ownerGuid] = p;
        return p;
    }

    public void Remove(ulong ownerGuid) => _byOwner.Remove(ownerGuid);
    public IEnumerable<Pet> All => _byOwner.Values;
}
