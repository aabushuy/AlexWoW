using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Авторитетная сущность существа в мире (M6.3): мутабельное здоровье, состояние жив/мёртв,
/// таймер респавна. Одна на GUID для всех наблюдателей (живёт в реестре <see cref="WorldState"/>),
/// в отличие от прежнего per-session <c>NpcSpawn</c>. Создаётся лениво из тех же DB-строк, что и
/// видимость. Позиция/шаблон неизменны (стационарный спавн); меняется только боевое состояние.
/// </summary>
public sealed class WorldCreature
{
    public required ulong Guid { get; init; }
    public required uint Map { get; init; }
    public required CreatureTemplate Template { get; init; }
    public required float X { get; init; }
    public required float Y { get; init; }
    public required float Z { get; init; }
    public required float O { get; init; }

    public required uint MaxHealth { get; init; }

    /// <summary>Текущее здоровье. 0 = мёртв (клиент рисует труп). Меняет боевой тик/респавн.</summary>
    public uint Health { get; set; }

    public bool IsAlive => Health > 0;

    /// <summary>Момент респавна (<see cref="Environment.TickCount64"/>, мс); null — пока жив.</summary>
    public long? RespawnAtMs { get; set; }

    // --- Ответный бой (M6.7 инкр.1): существо бьёт того, кто его атаковал, пока тот в мили-радиусе. ---
    /// <summary>GUID игрока, по которому существо отвечает (0 — не в бою). Читает/пишет серверный тик.</summary>
    public ulong CombatTargetGuid { get; set; }
    /// <summary>Момент следующего свинга существа (<see cref="Environment.TickCount64"/>, мс).</summary>
    public long NextSwingMs { get; set; }

    // --- Лут (M6.6): труп можно обыскать, пока есть нетронутый лут. ---
    /// <summary>Труп помечен lootable (UNIT_DYNAMIC_FLAGS) — есть что забрать. Сброс при респавне.</summary>
    public bool Lootable { get; set; }
    /// <summary>Сролленный лут трупа (null — пока не убит/уже разобран). M6.6.</summary>
    public CreatureLoot? Loot { get; set; }

    /// <summary>
    /// Здоровье существа по уровню (упрощённо — точные статы из creature_classlevelstats позже, M6+).
    /// Достаточно для убедимой полоски HP и боя в несколько свингов на низких уровнях.
    /// </summary>
    public static uint MaxHealthFor(byte level)
        => (uint)(25 + Math.Max((byte)1, level) * 12);
}
