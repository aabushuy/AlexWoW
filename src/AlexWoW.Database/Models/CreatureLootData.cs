namespace AlexWoW.Database.Models;

/// <summary>Лут-определение существа: диапазон денег + кандидаты-предметы (до ролла). M6.6.</summary>
public sealed record CreatureLootData
{
    public uint MinGold { get; init; }
    public uint MaxGold { get; init; }
    public IReadOnlyList<CreatureLootEntry> Drops { get; init; } = [];
}
