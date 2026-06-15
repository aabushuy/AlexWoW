using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// §8 On-hit яды разбойника: пока активен яд-бафф (эксклюзивная группа <see cref="SpellCatalog.GroupRoguePoison"/>),
/// прошедший мили-свинг по шансу прокает природный урон (Instant/Deadly/Wound). Crippling — чистый слоу (не
/// моделируем). Яды наносятся использованием яда-предмета на оружие (CMSG_USE_ITEM → спелл-применение, эффект 54);
/// бафф вешает <see cref="SpellCastCompletion"/>. Зеркало <see cref="ImbueService"/>/<see cref="SealService"/>.
/// Упрощения: Deadly-стек DoT, Wound-дебафф лечения, Crippling-слоу не моделируются — все дают разовый природный урон.
/// </summary>
internal sealed class PoisonService(KillRewardService killReward, ILogger<PoisonService> logger)
{
    // Ранги спеллов-применения ядов (эффект 54). Классификация по набору id (тип определяет прок).
    private static readonly HashSet<uint> Instant =
        [8679, 8686, 8688, 11338, 11339, 11340, 26891, 57967, 57968];
    private static readonly HashSet<uint> Deadly =
        [2823, 2824, 11355, 11356, 25351, 26967, 27186, 57972, 57973];
    private static readonly HashSet<uint> Wound =
        [13219, 13225, 13226, 13227, 27188, 57977, 57978];
    private static readonly HashSet<uint> Crippling = [3408, 11201];

    private const byte SchoolNature = 8; // SCHOOL_MASK_NATURE — школа ядов
    private const double ProcChance = 0.30; // шанс прока на удар (упрощённо для всех урон-ядов)

    /// <summary>Является ли спелл нанесением яда разбойника (любой ранг Instant/Deadly/Wound/Crippling).</summary>
    internal static bool IsPoison(uint spellId)
        => Instant.Contains(spellId) || Deadly.Contains(spellId) || Wound.Contains(spellId) || Crippling.Contains(spellId);

    /// <summary>Прок активного яда по прошедшему свингу. Возвращает true, если цель умерла от прок-урона.</summary>
    internal async Task<bool> OnMeleeHitAsync(WorldSession session, WorldCreature creature, long now, CancellationToken ct)
    {
        var poison = session.Progression.Auras.FirstOrDefault(a => IsPoison(a.SpellId));
        if (poison is null)
            return false;
        // Crippling — чистый слоу (не моделируем): урона нет.
        if (Crippling.Contains(poison.SpellId))
            return false;
        if (Random.Shared.NextDouble() >= ProcChance)
            return false;

        var level = (byte)(session.Character?.Level ?? 1);
        var amount = (uint)Random.Shared.Next(level, level * 2 + 1);
        var (_, overkill, died) = session.World.ApplyCreatureDamage(creature, amount);
        session.Combat.LastCombatMs = now;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgSpellNonMeleeDamageLog,
            SpellPackets.BuildDamageLog(creature.Guid, (ulong)session.InWorldGuid, poison.SpellId, amount, overkill, SchoolNature), ct);
        await session.World.BroadcastCreatureHealthAsync(creature, ct);
        if (died)
        {
            await killReward.OnCreatureKilledAsync(session, creature, ct);
            logger.LogDebug("POISON '{User}': яд {Spell} добил '{Name}'", session.Account, poison.SpellId, creature.Template.Name);
        }
        return died;
    }
}
