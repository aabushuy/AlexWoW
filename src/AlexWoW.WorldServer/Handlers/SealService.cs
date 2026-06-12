using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// On-hit прок печатей паладина (Фаза 2, шаг 2): по прошедшему мили-свингу активная печать даёт эффект —
/// Seal of Righteousness доп. holy-урон по цели, Seal of Light хил себе, Seal of Wisdom восстановление маны.
/// Печать активна = аура группы <see cref="SpellCatalog.GroupPaladinSeal"/> (эксклюзив, шаг 1). Величины
/// упрощённые (по уровню / % маны): движок без spell power/AP, а базовые точки проков коэффициентные (≈0).
/// Зовётся из <see cref="PlayerMeleeService"/> после нанесённого свинга.
/// </summary>
internal sealed class SealService(KillRewardService killReward, ManaRegenService manaRegen)
{
    // Печати тренера (дублируются в SpellCatalog.ExclusiveAuras с группой GroupPaladinSeal).
    private const uint SealOfRighteousness = 21084;
    private const uint SealOfLight = 20165;
    private const uint SealOfWisdom = 20166;
    // Seal of Justice 20164 — stun-proc, CC не моделируется (прок без эффекта).

    private const byte SchoolHoly = 2;
    private const uint PowerMana = 0; // POWER_MANA для лога энерджайза

    /// <summary>Прок активной печати по прошедшему свингу. Возвращает true, если цель умерла от прок-урона.</summary>
    internal async Task<bool> OnMeleeHitAsync(WorldSession session, WorldCreature creature, long now, CancellationToken ct)
    {
        var seal = session.Progression.Auras.FirstOrDefault(
            a => SpellCatalog.ExclusiveAuraGroup(a.SpellId) == SpellCatalog.GroupPaladinSeal);
        if (seal is null)
            return false;

        var level = (byte)(session.Character?.Level ?? 1);
        switch (seal.SpellId)
        {
            case SealOfRighteousness:
                return await ProcHolyDamageAsync(session, creature, level, now, ct);
            case SealOfLight:
                await ProcHealAsync(session, level, ct);
                return false;
            case SealOfWisdom:
                await ProcManaAsync(session, ct);
                return false;
            default:
                return false; // Justice и прочие — без моделируемого эффекта
        }
    }

    /// <summary>Seal of Righteousness: доп. holy-урон по цели (упрощённо ~уровень..2×уровень). Лог как спелл-урон.</summary>
    private async Task<bool> ProcHolyDamageAsync(WorldSession session, WorldCreature creature, byte level, long now, CancellationToken ct)
    {
        var amount = (uint)Random.Shared.Next(level, level * 2 + 1);
        var (_, overkill, died) = session.World.ApplyCreatureDamage(creature, amount);
        session.Combat.LastCombatMs = now;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgSpellNonMeleeDamageLog,
            SpellPackets.BuildDamageLog(creature.Guid, (ulong)session.InWorldGuid, SealOfRighteousness, amount, overkill, SchoolHoly), ct);
        await session.World.BroadcastCreatureHealthAsync(creature, ct);
        if (died)
        {
            await killReward.OnCreatureKilledAsync(session, creature, ct);
            session.Logger.LogDebug("SEAL '{User}': Seal of Righteousness добил '{Name}'", session.Account, creature.Template.Name);
        }
        return died;
    }

    /// <summary>Seal of Light: хил себе по удару (упрощённо). Виден зелёным числом (на манекене паладин ранен ответкой).</summary>
    private async Task ProcHealAsync(WorldSession session, byte level, CancellationToken ct)
    {
        if (session.Player is not { } player || session.Combat.IsDead)
            return;
        var amount = (uint)Random.Shared.Next(level, level * 2 + 1);
        var before = session.Combat.Health;
        session.Combat.Health = Math.Min(session.Combat.MaxHealth, before + amount);
        var effective = session.Combat.Health - before;
        var overheal = amount - effective;
        await session.World.BroadcastToPlayerObserversAsync(player, WorldOpcode.SmsgSpellHealLog,
            SpellPackets.BuildHealLog(player.Guid, (ulong)session.InWorldGuid, SealOfLight, effective, overheal), ct);
        await session.World.BroadcastPlayerHealthAsync(player, ct);
    }

    /// <summary>Seal of Wisdom: восстановление маны по удару — 3% макс. (как Bp прока 20168 = 3% базовой маны).
    /// Лог энерджайза шлём всегда (плавающее «+мана» видно даже при полной мане, как в WoW); полоску — если был прирост.</summary>
    private async Task ProcManaAsync(WorldSession session, CancellationToken ct)
    {
        if (session.Cast.MaxMana == 0 || session.Player is not { } player)
            return;
        var amount = Math.Max(1u, session.Cast.MaxMana * 3 / 100);
        var before = session.Cast.Mana;
        session.Cast.Mana = Math.Min(session.Cast.MaxMana, before + amount);
        await session.World.BroadcastToPlayerObserversAsync(player, WorldOpcode.SmsgSpellEnergizeLog,
            SpellPackets.BuildEnergizeLog(player.Guid, (ulong)session.InWorldGuid, SealOfWisdom, PowerMana, amount), ct);
        if (session.Cast.Mana != before)
            await manaRegen.SendManaUpdateAsync(session, ct);
    }
}
