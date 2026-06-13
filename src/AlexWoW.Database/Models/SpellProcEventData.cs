namespace AlexWoW.Database.Models;

/// <summary>
/// Строка <c>spell_proc_event</c> (БД мира): уточняет условия прока для спелла поверх Spell.dbc procFlags.
/// Нужна для крит-проков (PROC.2): <c>ProcEx</c> с битом PROC_EX_CRITICAL_HIT (0x02) требует крит триггера,
/// <c>SchoolMask</c> ограничивает школу триггера, <c>ProcFlags</c> (если != 0) переопределяет события из шаблона.
/// </summary>
public sealed record SpellProcEventData
{
    public uint Entry { get; init; }
    /// <summary>Маска школ триггера (0 — любая). Прок срабатывает только если школа спелла пересекается.</summary>
    public uint SchoolMask { get; init; }
    /// <summary>События прока (override Spell.dbc procFlags, если != 0).</summary>
    public uint ProcFlags { get; init; }
    /// <summary>Доп. условия (PROC_EX_*): бит 0x02 PROC_EX_CRITICAL_HIT — прок только на крит триггера.</summary>
    public uint ProcEx { get; init; }
}
