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

    /// <summary>
    /// Здоровье существа по уровню (упрощённо — точные статы из creature_classlevelstats позже, M6+).
    /// Достаточно для убедимой полоски HP и боя в несколько свингов на низких уровнях.
    /// </summary>
    public static uint MaxHealthFor(byte level)
        => (uint)(25 + Math.Max((byte)1, level) * 12);
}
