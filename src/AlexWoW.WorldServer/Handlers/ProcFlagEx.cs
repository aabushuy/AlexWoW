// Порт CMaNGOS-WoTLK: src/game/Spells/SpellMgr.h (enum ProcFlagsEx)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Spells/SpellMgr.h. GPL-2.0.

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Доп. условия прока (поле <c>spell_proc_event.procEx</c>): какой именно исход события подходит.
/// Один-в-один с CMaNGOS <c>ProcFlagsEx</c>. Если бит выставлен в <c>procEx</c> прок-ауры,
/// событие должно дать соответствующий исход (или его не было — но бит требуется).
/// </summary>
[System.Flags]
internal enum ProcFlagEx : uint
{
    None = 0x0000000,            // прокать на Hit/Crit (для пассивок без procEx)

    NormalHit = 0x0000001,            // обычный удар (не крит, прошёл)
    CriticalHit = 0x0000002,            // крит триггеранта (для крит-проков)

    Miss = 0x0000004,            // промах
    Resist = 0x0000008,            // полный резист
    Dodge = 0x0000010,            // увернулся
    Parry = 0x0000020,            // парировал
    Block = 0x0000040,            // заблокировал (частично)
    Evade = 0x0000080,            // увернулся (NPC evade)
    Immune = 0x0000100,            // иммунитет
    Deflect = 0x0000200,            // отклонил
    Absorb = 0x0000400,            // полностью погашено absorb-щитом
    Reflect = 0x0000800,            // отражено
    Interrupt = 0x0001000,            // прервал (не используется в CMaNGOS)
    FullBlock = 0x0002000,            // блок съел весь урон

    Reserved2 = 0x0004000,
    Reserved3 = 0x0008000,

    /// <summary>Прок срабатывает даже если урон/хил = 0 (нужно для проков «на касание»).</summary>
    TriggerOnNoDamage = 0x0010000,

    /// <summary>Прок один раз (не используется в CMaNGOS).</summary>
    OneTimeTrigger = 0x0020000,

    /// <summary>Periodic-эффект положительный (HoT). Без бита — DoT (negative).</summary>
    PeriodicPositive = 0x0040000,

    /// <summary>Прок на конец каста, не на хит.</summary>
    CastEnd = 0x0080000,

    /// <summary>Прок Grounding Totem (магнит).</summary>
    Magnet = 0x0100000,

    /// <summary>Internal — periodic heal. Не использовать в БД.</summary>
    InternalHot = 0x2000000,

    // Комбо-маски (CMaNGOS):
    /// <summary>Любой «промах»: avoid + immune + резист — событие не прошло.</summary>
    AnyMissEvent = Miss | Resist | Dodge | Parry | Block | Evade | Immune | Deflect | Reflect,
}
