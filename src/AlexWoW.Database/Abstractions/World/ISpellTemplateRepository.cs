using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>Спеллы из дампа Spell.dbc (<c>spell_template</c>, БД mangos). SRP-репозиторий (M10.2).</summary>
public interface ISpellTemplateRepository
{
    /// <summary>Данные спелла по id или null, если такого спелла нет в дампе.</summary>
    Task<SpellTemplateData?> GetSpellAsync(uint id, CancellationToken ct = default);

    /// <summary>Строка <c>spell_proc_event</c> по entry (уточнение условий прока) или null. Крит-проки (PROC.2).</summary>
    Task<SpellProcEventData?> GetProcEventAsync(uint id, CancellationToken ct = default);

    /// <summary>Пакетная загрузка спеллов по набору id (дедуп тиров профессий при входе). M11.</summary>
    Task<IReadOnlyList<SpellTemplateData>> GetSpellsAsync(IReadOnlyCollection<uint> ids, CancellationToken ct = default);

    /// <summary>Предыдущий ранг спелла из spell_chain (0 — ранг 1 / вне цепочки). Для SUPERCEDED. M10.3.</summary>
    Task<uint> GetPrevRankAsync(uint spellId, CancellationToken ct = default);

    /// <summary>Пакетно: spell_id → prev_spell из spell_chain (только спеллы с предыдущим рангом).
    /// Ранг-дедуп модификаторов талантов при входе. M10.6.</summary>
    Task<IReadOnlyDictionary<uint, uint>> GetPrevRanksAsync(IReadOnlyCollection<uint> spellIds, CancellationToken ct = default);

    /// <summary>Для seed-спеллов — все ОДНОИМЁННЫЕ спеллы (тот же SpellName, иной Id): пары (RankId, SeedId).
    /// Расширение рангов toggle/эксклюзивных аур на старте (seed-таблица держит один ранг, игрок кастует высший).</summary>
    Task<IReadOnlyList<(uint RankId, uint SeedId)>> GetSameNameRankIdsAsync(IReadOnlyCollection<uint> seedIds, CancellationToken ct = default);

    /// <summary>В пределах набора — пары (низший ранг → СЛЕДУЮЩИЙ известный ранг той же абилки), по
    /// SpellName + соседним SpellLevel (НЕ требует spell_chain — у физ-абилок он пуст). Множество <c>Lower</c> —
    /// это все «перекрытые» низшие ранги: их исключаем из INITIAL_SPELLS (в книгу шлём только активный высший
    /// ранг, как CMaNGOS), чтобы клиент свернул лестницу рангов и показал галку «Отображать все уровни».</summary>
    Task<IReadOnlyList<(uint Lower, uint Higher)>> GetRankSupersedePairsAsync(IReadOnlyCollection<uint> spellIds, CancellationToken ct = default);
}
