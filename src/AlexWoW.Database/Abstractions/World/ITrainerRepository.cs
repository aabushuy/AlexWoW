using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>Тренеры БД мира (npc_trainer[_template] + creature_template, дамп mangos). SRP-репозиторий (#25).</summary>
public interface ITrainerRepository
{
    /// <summary>Данные тренера по entry существа или null, если существо не тренер.</summary>
    Task<TrainerData?> GetTrainerAsync(uint entry, CancellationToken ct = default);
}
