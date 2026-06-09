using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>Тренеры БД мира (npc_trainer[_template] + creature_template, дамп mangos). SRP-репозиторий (#25).</summary>
public interface ITrainerRepository
{
    /// <summary>Данные тренера по entry существа или null, если существо не тренер.</summary>
    Task<TrainerData?> GetTrainerAsync(uint entry, CancellationToken ct = default);

    /// <summary>
    /// Entry классового тренера (TrainerType=0, TrainerClass=classId) для дев-команды <c>.trainer</c> (D1).
    /// Выбор data-driven: среди тренеров класса с ПРЯМЫМ списком спеллов (npc_trainer, как у наших дев-тренеров
    /// Faction=35/без расового гейта) предпочитаются дружелюбные ко всем (Faction=35) и с самым полным набором —
    /// чтобы команда работала любому персонажу класса (вкл. ДК) независимо от фракции/расы. null — нет тренера.
    /// </summary>
    Task<uint?> GetClassTrainerEntryAsync(byte classId, CancellationToken ct = default);

    /// <summary>
    /// Entry тренера профессии (TrainerType=2) для дев-команды <c>.proftrainer</c> (D2). В этом дампе нет
    /// <c>skill_line_ability</c>, поэтому профессию определяем по подписи существа: SubName LIKE
    /// '%<paramref name="subnameKeyword"/>%' (напр. «Blacksmith»). Берём самого ПОЛНОГО по набору
    /// (Grand Master) — фракция тренера не важна, dev-спавн делает его дружелюбным ко всем. null — не найден.
    /// </summary>
    Task<uint?> GetProfessionTrainerEntryAsync(string subnameKeyword, CancellationToken ct = default);
}
