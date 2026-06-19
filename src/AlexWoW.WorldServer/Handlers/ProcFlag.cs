// Порт CMaNGOS-WoTLK: src/game/Spells/SpellMgr.h (enum ProcFlags + комбо-маски)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Spells/SpellMgr.h. GPL-2.0.

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// События, на которых прок-ауры могут срабатывать (поле <c>spell_template.procFlags</c> +
/// override через <c>spell_proc_event.procFlags</c>). 25 битов — полное покрытие CMaNGOS-эталона.
/// </summary>
/// <remarks>
/// Имена один-в-один с CMaNGOS: <c>Deal*</c> — атакующая сторона, <c>Take*</c> — сторона жертвы.
/// «Swing» = авто-атака, «Ability» = спелл, использующий оружие (Devastate, Mortal Strike),
/// «Spell» = магический спелл (Frostbolt, Smite), «Periodic» = тик DoT/HoT.
/// </remarks>
[System.Flags]
internal enum ProcFlag : uint
{
    None = 0x00000000,

    Heartbeat = 0x00000001,   // 00 Heartbeat — каждый тик мира
    Kill = 0x00000002,   // 01 Убийство цели (с XP/Honor — см. IsTriggeredAtSpellProcEvent)

    DealMeleeSwing = 0x00000004,   // 02 Успешный мили-авто-удар
    TakeMeleeSwing = 0x00000008,   // 03 Получен мили-авто-удар

    DealMeleeAbility = 0x00000010,   // 04 Успешный спелл, использующий оружие (Devastate, Mortal Strike)
    TakeMeleeAbility = 0x00000020,   // 05 Получен спелл, использующий оружие

    DealRangedAttack = 0x00000040,   // 06 Успешный рейндж-авто-выстрел
    TakeRangedAttack = 0x00000080,   // 07 Получен рейндж-авто-выстрел

    DealRangedAbility = 0x00000100,   // 08 Успешный рейндж-спелл (Aimed Shot)
    TakeRangedAbility = 0x00000200,   // 09 Получен рейндж-спелл

    DealHelpfulAbility = 0x00000400,   // 10 Успешный положительный спелл с dmg class none
    TakeHelpfulAbility = 0x00000800,   // 11 Получен положительный спелл с dmg class none

    DealHarmfulAbility = 0x00001000,   // 12 Успешный негативный спелл с dmg class none
    TakeHarmfulAbility = 0x00002000,   // 13 Получен негативный спелл с dmg class none

    DealHelpfulSpell = 0x00004000,   // 14 Успешный каст положительного спелла (обычно хил)
    TakeHelpfulSpell = 0x00008000,   // 15 Получен положительный спелл (обычно хил)

    DealHarmfulSpell = 0x00010000,   // 16 Успешный каст негативного спелла (обычно урон)
    TakeHarmfulSpell = 0x00020000,   // 17 Получен негативный спелл (обычно урон)

    DealHarmfulPeriodic = 0x00040000,   // 18 Успешный тик DoT/HoT (знак см. procEx PERIODIC_POSITIVE)
    TakeHarmfulPeriodic = 0x00080000,   // 19 Получен тик DoT/HoT

    TakeAnyDamage = 0x00100000,   // 20 Получен любой урон
    OnTrapActivation = 0x00200000,   // 21 Активация ловушки хантера

    MainHandWeaponSwing = 0x00400000,   // 22 Успешный мили-удар главной рукой
    OffHandWeaponSwing = 0x00800000,   // 23 Успешный мили-удар второй рукой (DW)

    Death = 0x01000000,   // 24 На смерть владельца ауры

    // Комбо-маски для удобства фильтрации.
    MeleeBasedTriggerMask = DealMeleeSwing | TakeMeleeSwing
                          | DealMeleeAbility | TakeMeleeAbility
                          | DealRangedAttack | TakeRangedAttack
                          | DealRangedAbility | TakeRangedAbility,

    NegativeTriggerMask = MeleeBasedTriggerMask
                        | DealHarmfulAbility | TakeHarmfulAbility
                        | DealHarmfulSpell | TakeHarmfulSpell,
}
