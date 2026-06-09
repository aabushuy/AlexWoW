using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Репозиторий статусов квестов персонажа (таблица <c>character_queststatus</c>, БД <c>alexwow_auth</c>).
/// Часть DAL-фасада <see cref="ICharacterStore"/>. Срез 1 рефактора DAL (#23).
/// </summary>
public interface IQuestRepository
{
    /// <summary>Статусы квестов персонажа (активные + сданные).</summary>
    Task<IReadOnlyList<QuestStatusRow>> GetQuestStatusesAsync(uint ownerGuid, CancellationToken ct = default);

    /// <summary>Создаёт/обновляет статус квеста (accept/прогресс/сдача).</summary>
    Task UpsertQuestStatusAsync(uint ownerGuid, uint questId, byte slot, byte status,
        ushort c0, ushort c1, ushort c2, ushort c3, CancellationToken ct = default);
}
