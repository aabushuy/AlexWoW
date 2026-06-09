using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Квест-данные БД мира (quest_template + creature_questrelation/involvedrelation, дамп mangos).
/// SRP-репозиторий (#25). Отличается от character-side <see cref="IQuestRepository"/> (статусы игрока).
/// </summary>
public interface IQuestTemplateRepository
{
    /// <summary>Entry существ, дающих квесты (distinct creature_questrelation.id) — для иконок «!».</summary>
    Task<IReadOnlyList<uint>> GetQuestGiverEntriesAsync(CancellationToken ct = default);

    /// <summary>Entry существ, принимающих квесты (distinct creature_involvedrelation.id) — для иконок «?».</summary>
    Task<IReadOnlyList<uint>> GetQuestEnderEntriesAsync(CancellationToken ct = default);

    /// <summary>Все связи «дающий→квест» (creature_questrelation) — для кэша статуса иконок.</summary>
    Task<IReadOnlyList<QuestRelation>> GetQuestGiverRelationsAsync(CancellationToken ct = default);

    /// <summary>Все связи «приёмщик→квест» (creature_involvedrelation) — для кэша статуса иконок.</summary>
    Task<IReadOnlyList<QuestRelation>> GetQuestEnderRelationsAsync(CancellationToken ct = default);

    /// <summary>Квесты, которые даёт существо (creature_questrelation ⨝ quest_template).</summary>
    Task<IReadOnlyList<GiverQuest>> GetGiverQuestsAsync(uint creatureEntry, CancellationToken ct = default);

    /// <summary>Id квестов, которые ПРИНИМАЕТ существо (creature_involvedrelation). Для сдачи.</summary>
    Task<IReadOnlyList<uint>> GetEnderQuestIdsAsync(uint creatureEntry, CancellationToken ct = default);

    /// <summary>Полный шаблон квеста (quest_template) — детали/награды/цели.</summary>
    Task<QuestTemplateData?> GetQuestAsync(uint entry, CancellationToken ct = default);
}
