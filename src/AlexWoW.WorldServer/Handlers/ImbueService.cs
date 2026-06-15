using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// §8 On-hit оружейные имбу шамана: пока активен имбу-бафф (эксклюзивная группа
/// <see cref="SpellCatalog.GroupShamanImbue"/>), прошедший мили-свинг прокает эффект — Flametongue доп.
/// огненный урон (каждый удар), Frostbrand шанс фрост-урона, Windfury шанс доп. удара (бонус-урон).
/// Имбу — это энчанты оружия (эффект 54, без аур), поэтому накладываются особо (см. <see cref="SpellCastCompletion"/>):
/// видимый бафф через <see cref="AuraService"/>. Зеркало <see cref="SealService"/>. Величины упрощены
/// (движок без spell power/AP). Зовётся из <see cref="PlayerMeleeService"/> после нанесённого свинга.
/// </summary>
internal sealed class ImbueService(KillRewardService killReward, ILogger<ImbueService> logger)
{
    // Ранги имбу (SpellFamilyFlags одинаковы у всех трёх — классифицируем по id). Тип определяет прок.
    private static readonly HashSet<uint> Flametongue =
        [8024, 8027, 8030, 16339, 16341, 16342, 25489, 58785, 58789, 58790];
    private static readonly HashSet<uint> Frostbrand =
        [8033, 8038, 10456, 16355, 16356, 25500, 58794, 58795, 58796];
    private static readonly HashSet<uint> Windfury =
        [8232, 8235, 10486, 16362, 25505, 35886, 58801, 58803, 58804];

    private const byte SchoolFire = 4;   // SCHOOL_MASK_FIRE
    private const byte SchoolFrost = 16;  // SCHOOL_MASK_FROST
    private const byte SchoolPhysical = 1; // Windfury — доп. физ. удар

    private const double FrostbrandProcChance = 0.25; // шанс прока Frostbrand на удар
    private const double WindfuryProcChance = 0.20;    // шанс прока Windfury на удар

    /// <summary>Является ли спелл оружейным имбу шамана (любой ранг Flametongue/Frostbrand/Windfury).</summary>
    internal static bool IsImbue(uint spellId)
        => Flametongue.Contains(spellId) || Frostbrand.Contains(spellId) || Windfury.Contains(spellId);

    /// <summary>Прок активного имбу по прошедшему свингу. Возвращает true, если цель умерла от прок-урона.</summary>
    internal async Task<bool> OnMeleeHitAsync(WorldSession session, WorldCreature creature, long now, CancellationToken ct)
    {
        var imbue = session.Progression.Auras.FirstOrDefault(a => IsImbue(a.SpellId));
        if (imbue is null)
            return false;

        var level = (byte)(session.Character?.Level ?? 1);
        // Flametongue — огненный урон КАЖДЫЙ удар; Frostbrand/Windfury — по шансу.
        if (Flametongue.Contains(imbue.SpellId))
            return await ProcDamageAsync(session, creature, imbue.SpellId, SchoolFire, level, now, ct);
        if (Frostbrand.Contains(imbue.SpellId))
            return Random.Shared.NextDouble() < FrostbrandProcChance
                && await ProcDamageAsync(session, creature, imbue.SpellId, SchoolFrost, level, now, ct);
        if (Windfury.Contains(imbue.SpellId))
            return Random.Shared.NextDouble() < WindfuryProcChance
                && await ProcDamageAsync(session, creature, imbue.SpellId, SchoolPhysical, level, now, ct);
        return false;
    }

    /// <summary>Доп. урон имбу по цели (упрощённо ~уровень..2×уровень; spell power/AP не моделируем). Лог как
    /// спелл-урон школой имбу; добивание учитывает награду/лут.</summary>
    private async Task<bool> ProcDamageAsync(WorldSession session, WorldCreature creature, uint spellId,
        byte school, byte level, long now, CancellationToken ct)
    {
        var amount = (uint)Random.Shared.Next(level, level * 2 + 1);
        var (_, overkill, died) = session.World.ApplyCreatureDamage(creature, amount);
        session.Combat.LastCombatMs = now;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgSpellNonMeleeDamageLog,
            SpellPackets.BuildDamageLog(creature.Guid, (ulong)session.InWorldGuid, spellId, amount, overkill, school), ct);
        await session.World.BroadcastCreatureHealthAsync(creature, ct);
        if (died)
        {
            await killReward.OnCreatureKilledAsync(session, creature, ct);
            logger.LogDebug("IMBUE '{User}': имбу {Spell} добил '{Name}'", session.Account, spellId, creature.Template.Name);
        }
        return died;
    }
}
