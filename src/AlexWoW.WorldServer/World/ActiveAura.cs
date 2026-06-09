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
    /// <summary>Группа эксклюзивности переключателя (0=нет): стойки/формы, ауры паладина, аспекты охотника —
    /// наложение нового снимает прочие той же группы. M7 #21.</summary>
    public byte Group { get; init; }
    /// <summary>Перманентный переключатель — персистится через релог (стойка/аура/аспект/форма). M7 #21.</summary>
    public bool Persist { get; init; }
    /// <summary>Момент истечения (<see cref="System.Environment.TickCount64"/>, мс); 0 — перманентная.</summary>
    public long ExpiresAtMs { get; init; }
    /// <summary>Полная длительность (мс) для пакета (0 — без таймера).</summary>
    public int DurationMs { get; init; }
}
