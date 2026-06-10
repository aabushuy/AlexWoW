using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>Спеллы из дампа Spell.dbc (<c>spell_template</c>, БД mangos). SRP-репозиторий (M10.2).</summary>
public interface ISpellTemplateRepository
{
    /// <summary>Данные спелла по id или null, если такого спелла нет в дампе.</summary>
    Task<SpellTemplateData?> GetSpellAsync(uint id, CancellationToken ct = default);

    /// <summary>Пакетная загрузка спеллов по набору id (дедуп тиров профессий при входе). M11.</summary>
    Task<IReadOnlyList<SpellTemplateData>> GetSpellsAsync(IReadOnlyCollection<uint> ids, CancellationToken ct = default);

    /// <summary>Предыдущий ранг спелла из spell_chain (0 — ранг 1 / вне цепочки). Для SUPERCEDED. M10.3.</summary>
    Task<uint> GetPrevRankAsync(uint spellId, CancellationToken ct = default);
}
