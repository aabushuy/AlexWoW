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

    // Позиция мутабельна (M6.7 инкр.2: существо двигается при преследовании). Дом — точка спавна.
    public required float X { get; set; }
    public required float Y { get; set; }
    public required float Z { get; set; }
    public required float O { get; set; }

    public required float HomeX { get; init; }
    public required float HomeY { get; init; }
    public required float HomeZ { get; init; }
    public required float HomeO { get; init; }

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

    // --- Преследование/возврат (M6.7 инкр.2): движение по навмешу, leash на дом, реген. ---
    /// <summary>Существо возвращается на спавн (evade) — потеряло цель/ушло за leash. Регенит по прибытии.</summary>
    public bool Evading { get; set; }
    /// <summary>Момент следующего шага движения (троттлинг сплайнов). <see cref="Environment.TickCount64"/>, мс.</summary>
    public long NextMoveMs { get; set; }
    /// <summary>Момент следующего тика регена HP вне боя. <see cref="Environment.TickCount64"/>, мс.</summary>
    public long NextRegenMs { get; set; }
    /// <summary>Троттлинг доворота к цели в мили (фейсинг). <see cref="Environment.TickCount64"/>, мс. M6.7.</summary>
    public long NextFaceMs { get; set; }

    // --- Контроль (CC, Фаза 2): стан/рут/страх/немота/дезориентация от спелла игрока. ---
    /// <summary>Активный вид контроля (None — нет). Стан/страх/дезориентация мешают свингу (см. CreatureCombatAI).</summary>
    public SpellCatalog.CrowdControlKind CrowdControl { get; set; }
    /// <summary>Момент окончания контроля (<see cref="Environment.TickCount64"/>, мс).</summary>
    public long CrowdControlUntilMs { get; set; }
    /// <summary>Spell-id наложившего CC (для снятия визуальной ауры-дебаффа) + её слот.</summary>
    public uint CrowdControlSpellId { get; set; }
    public byte CrowdControlSlot { get; set; }

    // --- Каст существа (Фаза 2 INT.1): кастующий манекен крутит каст-бар; прерывается interrupt-спеллом игрока. ---
    /// <summary>Спелл, который существо сейчас кастует (0 — не кастует). Прерывание сбрасывает в 0.</summary>
    public uint CastingSpellId { get; set; }
    /// <summary>Школа текущего каста (для лока школы при прерывании).</summary>
    public byte CastSchoolMask { get; set; }
    /// <summary>Момент завершения текущего каста (<see cref="Environment.TickCount64"/>, мс).</summary>
    public long CastEndMs { get; set; }
    /// <summary>Когда существо начнёт следующий каст (пауза между кастами).</summary>
    public long NextCastMs { get; set; }
    /// <summary>Школа залочена прерыванием до этого момента (мс) — существо не может кастовать эту школу. INT.1.</summary>
    public long SchoolLockUntilMs { get; set; }
    /// <summary>Маска залоченных прерыванием школ (0 — нет лока).</summary>
    public byte SchoolLockMask { get; set; }

    // --- Снимаемый бафф существа (Фаза 2 DSP.2): один положительный бафф для проверки Purge/Spellsteal. ---
    /// <summary>Положительный бафф на существе (0 — нет). Снимается Purge / крадётся Spellsteal.</summary>
    public uint BuffSpellId { get; set; }
    /// <summary>Тип диспела баффа (1=Magic — для Purge/Spellsteal).</summary>
    public byte BuffDispelType { get; set; }
    /// <summary>Визуальный слот ауры-баффа на существе (для SMSG_AURA_UPDATE).</summary>
    public byte BuffSlot { get; set; }

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
