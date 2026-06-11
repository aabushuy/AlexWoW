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

    /// <summary>Пакетно: spell_id → prev_spell из spell_chain (только спеллы с предыдущим рангом).
    /// Ранг-дедуп модификаторов талантов при входе. M10.6.</summary>
    Task<IReadOnlyDictionary<uint, uint>> GetPrevRanksAsync(IReadOnlyCollection<uint> spellIds, CancellationToken ct = default);

    /// <summary>Из набора возвращает спеллы, которые суперсидятся БОЛЕЕ ВЫСОКИМ рангом того же спелла,
    /// присутствующим в наборе (тот же SpellName, реальный ранг Rank1&lt;&gt;'', больший SpellLevel) — т.е.
    /// низшие ранги. Для <c>.learnall</c>: учить только высший ранг цепочки. M7 #47.</summary>
    Task<IReadOnlySet<uint>> GetLowerRanksInSetAsync(IReadOnlyCollection<uint> spellIds, CancellationToken ct = default);
}
