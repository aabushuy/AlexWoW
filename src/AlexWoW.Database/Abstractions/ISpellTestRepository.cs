using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Репозиторий захвата проверки заклинаний (таблицы <c>spell_test_session</c>/<c>spell_test_result</c>,
/// БД alexwow_auth). M12 Spell QA. World-сервер пишет (старт/стоп сессии + результаты захвата), Web читает
/// (список сессий, результаты, анализ аномалий) и помечает проанализированные сессии с id тикета Vikunja.
/// </summary>
public interface ISpellTestRepository
{
    /// <summary>Создаёт заголовок сессии захвата, возвращает её id.</summary>
    Task<long> StartSessionAsync(uint ownerGuid, uint accountId, byte @class, byte level,
        SpellTestMode mode, bool talentsSlotted, string? note, CancellationToken ct = default);

    /// <summary>Помечает сессию завершённой (проставляет ended_at).</summary>
    Task EndSessionAsync(long sessionId, CancellationToken ct = default);

    /// <summary>Вставляет одну строку результата (ручной захват — по событию).</summary>
    Task AddResultAsync(SpellTestResult row, CancellationToken ct = default);

    /// <summary>Пакетная вставка результатов (харнесс — одним round-trip на спелл).</summary>
    Task AddResultsAsync(IReadOnlyList<SpellTestResult> rows, CancellationToken ct = default);

    /// <summary>Последние сессии захвата (по убыванию старта), не более <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<SpellTestSession>> GetSessionsAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>Сессия по id, либо null.</summary>
    Task<SpellTestSession?> GetSessionAsync(long sessionId, CancellationToken ct = default);

    /// <summary>Все результаты сессии (для анализа аномалий на админ-странице).</summary>
    Task<IReadOnlyList<SpellTestResult>> GetResultsAsync(long sessionId, CancellationToken ct = default);

    /// <summary>Помечает сессию проанализированной и проставляет id заведённого тикета Vikunja.</summary>
    Task MarkAnalyzedAsync(long sessionId, uint ticketId, CancellationToken ct = default);
}
