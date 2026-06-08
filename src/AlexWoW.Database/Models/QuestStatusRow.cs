namespace AlexWoW.Database.Models;

/// <summary>
/// Статус квеста персонажа (строка character_queststatus). M6.10 — персист квестов.
/// <c>Status</c>: 0 = активен (в журнале), 1 = сдан (rewarded). <c>Slot</c> — журнальный слот 0..24
/// (для сданных не важен). <c>Counter0..3</c> — прогресс целей-существ (kill/talk).
/// Complete/HasItemObjectives не хранятся — рекомпьютятся из quest_template при входе.
/// </summary>
public sealed class QuestStatusRow
{
    public uint QuestId { get; init; }
    public byte Slot { get; init; }
    public byte Status { get; init; }
    public ushort Counter0 { get; init; }
    public ushort Counter1 { get; init; }
    public ushort Counter2 { get; init; }
    public ushort Counter3 { get; init; }
}
