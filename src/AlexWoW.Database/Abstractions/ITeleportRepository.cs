using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Репозиторий точек телепорта dev-меню (таблица <c>dev_teleport</c>, БД alexwow_auth). Узкий интерфейс
/// (ISP): источник для серверного каталога меню аддона и dev-команды <c>.tp</c>. Devcommands (#79).
/// </summary>
public interface ITeleportRepository
{
    /// <summary>Все точки телепорта, отсортированные по <see cref="TeleportLocation.SortOrder"/>.</summary>
    Task<IReadOnlyList<TeleportLocation>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Точка телепорта по идентификатору, либо null.</summary>
    Task<TeleportLocation?> GetByIdAsync(uint id, CancellationToken ct = default);
}
