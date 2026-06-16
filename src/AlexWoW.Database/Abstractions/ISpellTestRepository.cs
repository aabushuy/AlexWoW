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

    // --- QA T1 (Vikunja 185): очередь внешних запросов на авто-прогон харнесса (DB-flag + World-tick) ---

    /// <summary>
    /// Создаёт запрос на авто-прогон для онлайн-персонажа аккаунта <paramref name="account"/>. Возвращает id
    /// строки-запроса — Claude/Web затем опрашивает <see cref="GetRequestAsync"/> до статуса done (session_id).
    /// </summary>
    Task<long> EnqueueRequestAsync(string account, int casts, string? note, CancellationToken ct = default);

    /// <summary>
    /// Выбирает ОДНУ pending-строку (FIFO по requested_at) и атомарно переводит её в running (CAS pending→running).
    /// Возвращает её (id/account/casts/note) либо null, если pending-запросов нет. Зовётся World-tick'ом.
    /// </summary>
    Task<SpellTestRequestClaim?> ClaimPendingRequestAsync(CancellationToken ct = default);

    /// <summary>
    /// Финализирует запрос: статус done + проставляет <paramref name="sessionId"/> созданной spell_test_session
    /// (Claude читает его SELECT'ом). Либо failed + <paramref name="error"/> (нет онлайн-сессии/исключение).
    /// </summary>
    Task CompleteRequestAsync(long requestId, bool success, long? sessionId, string? error, CancellationToken ct = default);

    /// <summary>Строка-запрос по id (Claude/Web опрашивает до done/failed). null — нет такой строки.</summary>
    Task<SpellTestRequestView?> GetRequestAsync(long requestId, CancellationToken ct = default);
}
