namespace AlexWoW.Database.Models;

/// <summary>Характеристика предмета (stat_type/value) — пара для ItemStat в item-query.</summary>
public readonly record struct ItemStat(uint Type, int Value);
