namespace AlexWoW.WorldServer.World;

/// <summary>
/// Активная аура на игроке (M6.11): бафф/дебафф/форма. Слот — позиция в баф-баре клиента (визуальный
/// слот SMSG_AURA_UPDATE). ShapeshiftForm != 0 — аура-форма (стойка/форма друида) → влияет на
/// UNIT_FIELD_BYTES_2. ExpiresAtMs = 0 — перманентная (снимается явно).
/// </summary>
public sealed class ActiveAura
{
    public required uint SpellId { get; init; }
    public required byte Slot { get; init; }
    public required byte Flags { get; init; }
    /// <summary>Форма шейпшифта (стойка), если аура её задаёт; иначе 0. M6.11.</summary>
    public byte ShapeshiftForm { get; init; }
    /// <summary>Момент истечения (<see cref="System.Environment.TickCount64"/>, мс); 0 — перманентная.</summary>
    public long ExpiresAtMs { get; init; }
    /// <summary>Полная длительность (мс) для пакета (0 — без таймера).</summary>
    public int DurationMs { get; init; }
}
