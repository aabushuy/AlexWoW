using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>Лут существ БД мира (creature_loot_template, дамп mangos). SRP-репозиторий (#25).</summary>
public interface ILootRepository
{
    /// <summary>Лут-определение существа (деньги + кандидаты-предметы) или null, если лута нет.</summary>
    Task<CreatureLootData?> GetCreatureLootAsync(uint creatureEntry, CancellationToken ct = default);
}
