namespace AlexWoW.Database.Models;

/// <summary>
/// Тренер: тип (0=класс, 2=профессия), гейтинг по классу/расе (creature_template) + ассортимент
/// (npc_trainer ∪ npc_trainer_template) + приветствие (trainer_greeting). M9.3.
/// </summary>
public sealed record TrainerData
{
    public byte TrainerType { get; init; }
    public byte TrainerClass { get; init; }
    public byte TrainerRace { get; init; }
    public string Greeting { get; init; } = string.Empty;
    public IReadOnlyList<TrainerSpell> Spells { get; init; } = [];
}
