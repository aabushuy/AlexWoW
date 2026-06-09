namespace AlexWoW.Database.Models;

/// <summary>Строка лут-таблицы существа (creature_loot_template ⨝ item_template) — кандидат на дроп. M6.6.</summary>
public sealed record CreatureLootEntry
{
    public uint ItemId { get; init; }
    public float Chance { get; init; }     // ChanceOrQuestChance: шанс дропа (%), >0 — обычный предмет
    public int MinCount { get; init; }     // mincountOrRef: >0 — мин. количество (отрицательное — ссылка, пропускаем)
    public uint MaxCount { get; init; }
    public uint DisplayId { get; init; }
}
