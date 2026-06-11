namespace AlexWoW.Database.Models;

/// <summary>
/// Запись применённого эффекта заклинания в сессии захвата (строка <c>spell_test_result</c>). Иммутабельный
/// DTO — используется и как вход для вставки (Id=0), и как результат чтения. Эталон (<see cref="ExpectedMin"/>/
/// <see cref="ExpectedMax"/>, школа, стоимость) сохранён в момент захвата — Web анализирует без доступа к mangos.
/// </summary>
public sealed record SpellTestResult
{
    public long Id { get; init; }
    public long SessionId { get; init; }
    public uint SpellId { get; init; }
    public byte Class { get; init; }
    public byte Level { get; init; }
    public SpellTestResultType ResultType { get; init; }
    public byte School { get; init; }
    public uint Amount { get; init; }
    public uint Effective { get; init; }
    public uint OverkillOrOverheal { get; init; }
    public uint ExpectedMin { get; init; }
    public uint ExpectedMax { get; init; }
    public uint ExpectedCost { get; init; }
    public byte PowerType { get; init; }
    public bool IsHeal { get; init; }
    public bool WeaponBased { get; init; }
    public uint FamilyName { get; init; }
    public ushort CastIndex { get; init; }
    public DateTime RecordedAt { get; init; }
}
