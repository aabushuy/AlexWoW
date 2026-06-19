namespace AlexWoW.Database.Entities;

/// <summary>
/// EF-сущность таблицы <c>spell_test_session</c> (БД alexwow_auth) — заголовок сессии захвата проверки
/// заклинаний (M12 Spell QA). Одна сессия = один прогон тестировщика (ручной или авто-харнесс) по
/// манекенам; результаты — в <see cref="SpellTestResult"/>. Анализ и заведение тикета — на админ-странице Web.
/// </summary>
public sealed class SpellTestSession
{
    public long Id { get; set; }            // PK (auto-increment)
    public uint OwnerGuid { get; set; }     // guid тестового персонажа
    public uint AccountId { get; set; }     // аккаунт тестировщика
    public byte Class { get; set; }         // класс персонажа на старте (Chr classes 1..11)
    public byte Level { get; set; }         // уровень на старте
    public byte Mode { get; set; }          // 0 = ручной (Manual), 1 = авто-харнесс (Harness)
    public byte TalentsSlotted { get; set; }// 1 = на старте были активны мод-таланты (baseline должен быть 0)
    public DateTime StartedAt { get; set; } // время старта (UTC)
    public DateTime? EndedAt { get; set; }  // время остановки (UTC); null — ещё идёт
    public string? Note { get; set; }       // опциональная метка из .spelltest start <note>
    public byte Analyzed { get; set; }      // 1 = проанализирована и заведён тикет
    public uint? TicketId { get; set; }     // id задачи во внешнем трекере (если заведён по аномалиям)
}
