namespace AlexWoW.Database.Models;

/// <summary>Заголовок сессии захвата проверки заклинаний (строка <c>spell_test_session</c>). Иммутабельный DTO.</summary>
public sealed record SpellTestSession
{
    public long Id { get; init; }
    public uint OwnerGuid { get; init; }
    public uint AccountId { get; init; }
    public byte Class { get; init; }
    public byte Level { get; init; }
    public SpellTestMode Mode { get; init; }
    public bool TalentsSlotted { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public string? Note { get; init; }
    public bool Analyzed { get; init; }
    public uint? TicketId { get; init; }
}
