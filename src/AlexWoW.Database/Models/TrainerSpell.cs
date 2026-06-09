namespace AlexWoW.Database.Models;

/// <summary>
/// Одна обучаемая абилка тренера (строка npc_trainer / npc_trainer_template). M9.3.
/// spell — изучаемый спелл; цена в меди; требования по уровню/скиллу/предыдущим абилкам.
/// </summary>
public sealed record TrainerSpell
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
