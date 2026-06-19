namespace AlexWoW.Database.Models;

/// <summary>Режим сессии захвата проверки заклинаний (M12 Spell QA).</summary>
public enum SpellTestMode : byte
{
    /// <summary>Ручной: тестировщик сам кастует по манекенам (.spelltest start/stop).</summary>
    Manual = 0,

    /// <summary>Авто-харнесс: сервер прогоняет все абилки класса (.spelltest run).</summary>
    Harness = 1,
}

/// <summary>Тип записанного эффекта в строке результата (M12 Spell QA).</summary>
public enum SpellTestResultType : byte
{
    DirectDamage = 0,
    DirectHeal = 1,
    DotTick = 2,
    HotTick = 3,
}

/// <summary>
/// Статус строки-запроса на авто-прогон харнесса (таблица spell_test_request, QA T1).
/// Жизненный цикл: pending → running → done/failed. World-tick делает CAS pending→running (claim), затем
/// финализирует: done (проставлен session_id) либо failed (проставлен error).
/// </summary>
public enum SpellTestRequestStatus : byte
{
    Pending = 0,  // вставлен Web/Claude, ждёт выборки World-tick'ом
    Running = 1,  // захвачен World-tick'ом, прогон идёт
    Done = 2,     // прогон завершён, session_id проставлен
    Failed = 3,   // провал (нет онлайн-сессии/персонаж не в мире/исключение), error проставлен
}
