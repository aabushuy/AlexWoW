using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>Таланты БД мира (talent ⨝ talent_tab, дамп DBC). SRP-репозиторий (M9.7). Read-only, кэш.</summary>
public interface ITalentRepository
{
    /// <summary>Все таланты по talentId (с класс-маской дерева). Кэшируется при первом обращении.</summary>
    Task<IReadOnlyDictionary<uint, TalentData>> GetAllTalentsAsync(CancellationToken ct = default);
}
