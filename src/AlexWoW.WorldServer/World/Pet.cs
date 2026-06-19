// Порт CMaNGOS-WoTLK: src/game/Entities/Pet.cpp + Pet.h
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Entities/Pet.cpp. GPL-2.0.

namespace AlexWoW.WorldServer.World;

/// <summary>Типы пета (CMaNGOS PetType).</summary>
internal enum PetType : byte
{
    Summon = 0,     // призыв (Warlock imp/voidwalker/succubus/felhunter/felguard, водный элементаль мага, дух волка шамана)
    Hunter = 1,     // хантер pet (приручённый)
    Guardian = 2,   // временный strawman (Mirror Image, Treants и т.п.)
    Mini = 3,       // companion (питомец-компаньон, не боевой)
}

/// <summary>Режим реакции пета (CMaNGOS ReactStates). Параметр T2.</summary>
internal enum PetReactState : byte
{
    Passive = 0,
    Defensive = 1,
    Aggressive = 2,
}

/// <summary>Команда пету от клиента (CMaNGOS CommandStates). Параметр T2.</summary>
internal enum PetCommandState : byte
{
    Stay = 0,
    Follow = 1,
    Attack = 2,
    Abandon = 3,    // (T1: для PET_ABANDON опкода)
    MoveTo = 4,
}

/// <summary>
/// Пет игрока: ссылка на хозяина + creature_template entry + state.
/// T1 покрывает: state + summon. T2 PetAI. T3 actions. T4 Hunter taming/talents. T5 persistence (выполнено).
/// </summary>
internal sealed class Pet
{
    public uint Id { get; init; }       // in-memory id
    public uint PersistedId { get; set; } // PK character_pet

    public required ulong OwnerGuid { get; init; }
    public required uint Entry { get; init; }  // creature_template.entry
    public string Name { get; set; } = "";
    public byte Level { get; set; }
    public uint Experience { get; set; }
    public uint Health { get; set; }
    public uint MaxHealth { get; set; }
    public uint Mana { get; set; }
    public uint MaxMana { get; set; }
    public PetType Type { get; set; } = PetType.Summon;
    public PetReactState ReactState { get; set; } = PetReactState.Defensive;
    public PetCommandState CommandState { get; set; } = PetCommandState.Follow;
    public uint Happiness { get; set; }
    public DateTime SummonedAt { get; init; } = DateTime.UtcNow;
    /// <summary>GUID существа-пета в мире (если ещё в мире). 0 — деспавн.</summary>
    public ulong CreatureGuid { get; set; }
}
