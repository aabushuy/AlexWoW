using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Контроль (CC, Фаза 2): наложение стана/рута/страха/немоты/дезориентации на существо-цель спелла.
/// Тип/длительность — из <see cref="SpellCatalog.SpellInfo.CrowdControl"/> (data-driven по CC-ауре). Визуал —
/// аура-дебафф на существе (SMSG_AURA_UPDATE) + UNIT_FIELD_FLAGS (звёзды стана/страх/немота). Enforcement —
/// в <see cref="CreatureCombatAI"/>: стан/страх/дезориентация мешают свингу. Истечение — в тике существа.
/// PvE-фокус: цель — существо; рут/немота визуальны (существа не ходят/редко кастуют), стан/страх блокируют бой.
/// </summary>
internal sealed class CrowdControlService(ILogger<CrowdControlService> logger)
{
    /// <summary>Зарезервированный слот ауры CC на существе (выше DoT-слотов 0..N, чтобы не сталкиваться).</summary>
    private const byte CcAuraSlot = 40;

    /// <summary>UNIT_FLAG для клиентского визуала по типу CC (рут — без флага, клиент по ауре MOD_ROOT).</summary>
    private static uint UnitFlagFor(SpellCatalog.CrowdControlKind k) => k switch
    {
        SpellCatalog.CrowdControlKind.Stun => UnitFlags.Stunned,
        SpellCatalog.CrowdControlKind.Fear => UnitFlags.Fleeing,
        SpellCatalog.CrowdControlKind.Disorient => UnitFlags.Confused,
        SpellCatalog.CrowdControlKind.Silence => UnitFlags.Silenced,
        _ => 0, // Root — без unit-флага
    };

    /// <summary>Мешает ли активный CC существу действовать (свинг/ответка): стан/страх/дезориентация.</summary>
    internal static bool PreventsAction(WorldCreature c, long now)
        => c.CrowdControl is SpellCatalog.CrowdControlKind.Stun
            or SpellCatalog.CrowdControlKind.Fear
            or SpellCatalog.CrowdControlKind.Disorient
           && now < c.CrowdControlUntilMs;

    /// <summary>§4: обездвижено ли существо рутом (Frost Nova) — не преследует, но бьёт в упор. Свинг рут не блокирует.</summary>
    internal static bool IsRooted(WorldCreature c, long now)
        => c.CrowdControl == SpellCatalog.CrowdControlKind.Root && now < c.CrowdControlUntilMs;

    /// <summary>Накладывает CC на существо (после прямого эффекта каста): состояние + визуал ауры + UNIT_FLAG.
    /// <paramref name="durationOverrideMs"/>&gt;0 — взять вместо базовой длительности (CP.3b: стан-финишер
    /// Kidney Shot, длительность от очков серии; у ранга 1 base=0, поэтому работает только через override).</summary>
    internal async Task ApplyAsync(WorldSession session, WorldCreature creature, uint spellId,
        SpellCatalog.SpellInfo info, long now, CancellationToken ct, int durationOverrideMs = 0)
    {
        var durationMs = durationOverrideMs > 0 ? durationOverrideMs : info.CrowdControlMs;
        if (info.CrowdControl == SpellCatalog.CrowdControlKind.None || durationMs <= 0
            || !creature.IsAlive || session.InWorldGuid == 0)
            return;

        if (creature.CrowdControlSpellId != 0)
            await RemoveVisualAsync(session.World, creature, ct); // освежить: снять прежний CC-визуал

        creature.CrowdControl = info.CrowdControl;
        creature.CrowdControlUntilMs = now + durationMs;
        creature.CrowdControlSpellId = spellId;
        creature.CrowdControlSlot = CcAuraSlot;

        var level = (byte)(session.Character?.Level ?? 1);
        const byte Flags = AuraFlags.Effect1 | AuraFlags.Negative | AuraFlags.Duration;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAuraUpdate,
            AuraPackets.BuildApplyByCaster(creature.Guid, (ulong)session.InWorldGuid, CcAuraSlot, spellId,
                Flags, level, 1, durationMs), ct);

        var flag = UnitFlagFor(info.CrowdControl);
        if (flag != 0)
            await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgUpdateObject,
                CreatureUpdate.BuildUnitFlagsUpdate(creature.Guid, flag), ct);

        logger.LogDebug("CC '{User}': {Kind} на '{Name}' на {Ms}мс", session.Account, info.CrowdControl,
            creature.Template.Name, durationMs);
    }

    /// <summary>§4 CC по площади (Frost Nova/Psychic Scream): накладывает CC на всех живых враждебных существ
    /// в радиусе вокруг игрока (либо тех, кто уже в бою с ним). Радиус упрощён (DBC SpellRadius не загружен).</summary>
    private const float AreaCcRadius = 10f;
    internal async Task ApplyAreaAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        long now, CancellationToken ct, int durationOverrideMs = 0)
    {
        if (session.Player is not { } player)
            return;
        var playerFt = DisplayData.FactionForRace(player.Character.Race);
        foreach (var creature in session.Visibility.VisibleNpcs.Values.ToList())
        {
            if (!creature.IsAlive)
                continue;
            var dx = creature.X - player.X;
            var dy = creature.Y - player.Y;
            var dz = creature.Z - player.Z;
            if (dx * dx + dy * dy + dz * dz > AreaCcRadius * AreaCcRadius)
                continue;
            // Цель AoE-CC: враждебное по фракции ИЛИ уже атакующее игрока (тест-манекен «.dummy attack»).
            if (!session.World.IsHostile(creature.Template.Faction, playerFt) && creature.CombatTargetGuid != player.Guid)
                continue;
            await ApplyAsync(session, creature, spellId, info, now, ct, durationOverrideMs);
        }
    }

    /// <summary>Снимает истёкший CC (визуал + флаги + состояние). Зовётся из тика боя существа.</summary>
    internal async Task ExpireIfDueAsync(WorldState world, WorldCreature creature, long now, CancellationToken ct)
    {
        if (creature.CrowdControl == SpellCatalog.CrowdControlKind.None || now < creature.CrowdControlUntilMs)
            return;
        await RemoveVisualAsync(world, creature, ct);
        creature.CrowdControl = SpellCatalog.CrowdControlKind.None;
        creature.CrowdControlSpellId = 0;
    }

    /// <summary>Снимает CC-визуал существа: аура-дебафф + сброс UNIT_FIELD_FLAGS.</summary>
    private static async Task RemoveVisualAsync(WorldState world, WorldCreature creature, CancellationToken ct)
    {
        await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAuraUpdate,
            AuraPackets.BuildRemove(creature.Guid, creature.CrowdControlSlot), ct);
        await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgUpdateObject,
            CreatureUpdate.BuildUnitFlagsUpdate(creature.Guid, 0), ct);
    }
}
