namespace AlexWoW.Database.Models;

/// <summary>
/// Одна обучаемая абилка тренера (строка npc_trainer / npc_trainer_template). M9.3.
/// spell — изучаемый спелл; цена в меди; требования по уровню/скиллу/предыдущим абилкам.
/// </summary>
public sealed class TrainerSpell
{
    public uint Spell { get; init; }
    public uint SpellCost { get; init; }
    public ushort ReqSkill { get; init; }
    public ushort ReqSkillValue { get; init; }
    public byte ReqLevel { get; init; }
    public uint ReqAbility1 { get; init; }
    public uint ReqAbility2 { get; init; }
    public uint ReqAbility3 { get; init; }
}

/// <summary>
/// Тренер: тип (0=класс, 2=профессия), гейтинг по классу/расе (creature_template) + ассортимент
/// (npc_trainer ∪ npc_trainer_template) + приветствие (trainer_greeting). M9.3.
/// </summary>
public sealed class TrainerData
{
    public byte TrainerType { get; init; }
    public byte TrainerClass { get; init; }
    public byte TrainerRace { get; init; }
    public string Greeting { get; init; } = string.Empty;
    public IReadOnlyList<TrainerSpell> Spells { get; init; } = [];
}
