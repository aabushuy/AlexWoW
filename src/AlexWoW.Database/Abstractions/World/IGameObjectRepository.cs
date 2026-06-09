using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>Гейм-объекты БД мира (gameobject + gameobject_template, дамп mangos). SRP-репозиторий (#25).</summary>
public interface IGameObjectRepository
{
    /// <summary>Видимые гейм-объекты на карте в квадрате ±range (только с моделью).</summary>
    Task<IReadOnlyList<GameObjectSpawnData>> GetGameObjectsNearAsync(
        uint map, float x, float y, float range, int limit, CancellationToken ct = default);

    /// <summary>Шаблон гейм-объекта по entry (для CMSG_GAMEOBJECT_QUERY).</summary>
    Task<GameObjectTemplateData?> GetGameObjectTemplateAsync(uint entry, CancellationToken ct = default);
}
