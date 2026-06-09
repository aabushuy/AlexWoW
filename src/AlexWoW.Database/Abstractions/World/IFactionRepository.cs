using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>Реакции фракций БД мира (faction_template из FactionTemplate.dbc). SRP-репозиторий (#25).</summary>
public interface IFactionRepository
{
    /// <summary>Все реакции фракций (faction_template) для серверного авто-агро.</summary>
    Task<IReadOnlyList<FactionTemplateRow>> GetFactionTemplatesAsync(CancellationToken ct = default);
}
