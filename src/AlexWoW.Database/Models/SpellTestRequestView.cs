namespace AlexWoW.Database.Models;

/// <summary>
/// Захваченный World-tick'ом запрос на авто-прогон харнесса (таблица spell_test_request, задача Vikunja 185 / QA T1).
/// Возвращается <c>ClaimPendingRequestAsync</c>: только поля, нужные обработчику (id + параметры прогона).
/// Иммутабельный DTO.
/// </summary>
public sealed record SpellTestRequestClaim
{
    public long Id { get; init; }
    public string Account { get; init; } = "";
    public int Casts { get; init; }
    public string? Note { get; init; }
}

/// <summary>
/// Строка-запрос на авто-прогон харнесса (таблица spell_test_request) для опроса статуса. Claude/Web вставляет
/// pending-строку, затем опрашивает <c>GetRequestAsync</c> до status=done (видит session_id) или failed (видит error).
/// Иммутабельный DTO.
/// </summary>
public sealed record SpellTestRequestView
{
    public long Id { get; init; }
    public string Account { get; init; } = "";
    public int Casts { get; init; }
    public string? Note { get; init; }
    public SpellTestRequestStatus Status { get; init; }
    public long? SessionId { get; init; }     // id spell_test_session при успехе — Claude читает это
    public string? Error { get; init; }        // причина провала при status=Failed
    public DateTime RequestedAt { get; init; }
    public DateTime? ProcessedAt { get; init; }
}
