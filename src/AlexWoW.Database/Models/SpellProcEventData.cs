namespace AlexWoW.Database.Models;

/// <summary>
/// Строка <c>spell_proc_event</c> (БД мира): уточняет условия прока для спелла поверх Spell.dbc procFlags.
/// Эталон — CMaNGOS <c>SpellProcEventEntry</c> (src/game/Spells/SpellMgr.h).
/// </summary>
public sealed record SpellProcEventData
{
    public uint Entry { get; init; }

    /// <summary>Маска школ триггера (0 — любая). Прок срабатывает только если школа спелла пересекается.</summary>
    public uint SchoolMask { get; init; }

    /// <summary>События прока (override Spell.dbc procFlags, если != 0).</summary>
    public uint ProcFlags { get; init; }

    /// <summary>Доп. условия (PROC_EX_*): полный 22-битный bitmask (T2). 0 — без условий.</summary>
    public uint ProcEx { get; init; }

    // --- T5: spell-family фильтр триггеранта ---
    /// <summary>SpellFamilyName триггеранта (0 — без family-фильтра). Должен совпасть с триггерующим спеллом.</summary>
    public uint SpellFamilyName { get; init; }

    /// <summary>Биты 0–31 family-mask эффекта 0 (A0). Используется в комбинации с MaskA1.</summary>
    public uint SpellFamilyMaskA0 { get; init; }
    /// <summary>Биты 32–63 family-mask эффекта 0 (A1).</summary>
    public uint SpellFamilyMaskA1 { get; init; }
    /// <summary>Биты 64–95 family-mask эффекта 0 (A2).</summary>
    public uint SpellFamilyMaskA2 { get; init; }

    // --- T4: PPM (proc-per-minute) для weapon-based проков ---
    /// <summary>Шанс прока в минуту для weapon-based проков (Mongoose Bite, Crusader). 0 — fallback на procChance.</summary>
    public float PpmRate { get; init; }

    /// <summary>Override шанс (в процентах) — приоритет над Spell.dbc procChance, ниже PpmRate. 0 — не задан.</summary>
    public float CustomChance { get; init; }

    // --- T3: ICD (proc internal cooldown) ---
    /// <summary>Скрытый кулдаун прока в миллисекундах (0 — без ICD).</summary>
    public uint Cooldown { get; init; }
}
