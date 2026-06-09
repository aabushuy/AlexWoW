using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>Существа БД мира (creature + creature_template, дамп mangos). SRP-репозиторий (#25).</summary>
public interface ICreatureRepository
{
    /// <summary>Проверка доступности БД мира при старте (есть ли таблица creature).</summary>
    Task<long> CountCreaturesAsync(CancellationToken ct = default);

    /// <summary>Спавны существ на карте в квадрате ±range от точки (грубая зона видимости).</summary>
    Task<IReadOnlyList<CreatureSpawnData>> GetCreaturesNearAsync(
        uint map, float x, float y, float range, int limit, CancellationToken ct = default);

    /// <summary>Шаблон существа по entry (для CMSG_CREATURE_QUERY).</summary>
    Task<CreatureTemplateData?> GetCreatureTemplateAsync(uint entry, CancellationToken ct = default);
}
