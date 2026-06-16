namespace AlexWoW.Database.Entities;

/// <summary>
/// EF-сущность таблицы <c>spell_test_request</c> (БД alexwow_auth) — очередь внешних запросов на авто-прогон
/// харнесса проверки заклинаний (M12 Spell QA, задача Vikunja 185 / QA T1). Web/Claude вставляет строку
/// (status=pending), World-tick подхватывает её и запускает <c>SpellTestHarnessService.RunAsync</c> для
/// онлайн-сессии указанного аккаунта, затем пишет результат (session_id/error) и статус done/failed.
/// Заменяет чат-команду <c>.spelltest run</c> для hands-off режима: Claude инициирует прогон SQL-INSERT'ом,
/// читает session_id SELECT'ом — без клиента и без ввода в чат.
/// </summary>
public sealed class SpellTestRequest
{
    public long Id { get; set; }            // PK (auto-increment)
    public string Account { get; set; } = ""; // имя аккаунта онлайн-персонажа-тестировщика (target)
    public int Casts { get; set; }          // castsPerSpell (clamp 1..50 в харнессе); 0 — по умолчанию (5)
    public string? Note { get; set; }       // опциональная метка сессии
    public byte Status { get; set; }        // 0=pending, 1=running, 2=done, 3=failed (SpellTestRequestStatus)
    public long? SessionId { get; set; }    // id созданной spell_test_session (при успехе) — Claude читает это
    public string? Error { get; set; }      // причина провала (нет онлайн-сессии/персонаж не в мире/исключение)
    public DateTime RequestedAt { get; set; } // время INSERT'а (UTC)
    public DateTime? ProcessedAt { get; set; } // время завершения обработки (UTC); null — ещё не обработан
}
