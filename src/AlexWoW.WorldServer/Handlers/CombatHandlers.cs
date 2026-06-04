using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Бой (M6.3): выбор цели, авто-атака мили по NPC, расчёт урона, смерть и стоп.
/// Свинги продвигает серверный тик (<see cref="WorldState.UpdateAsync"/> → <see cref="TickMeleeAsync"/>);
/// существо — авторитетная <see cref="WorldCreature"/> из реестра мира (общее HP для наблюдателей).
/// Ответный удар NPC и преследование — M6.7.
/// </summary>
public static class CombatHandlers
{
    /// <summary>Интервал мили-свинга (мс). Упрощённо — без учёта скорости оружия (точнее в M6.4+).</summary>
    private const long SwingIntervalMs = 2000;

    /// <summary>
    /// Мили-радиус (ярды) центр-в-центр. ≈ NOMINAL_MELEE_RANGE: combat reach игрока (~1.5) +
    /// существа (~1.5) + запас. Упрощённо плоской константой (точная формула с combat reach — позже).
    /// </summary>
    private const float MeleeRangeYards = 5.0f;

    /// <summary>HitInfo для обычного удара: AFFECTS_VICTIM (0x2) — без absorb/resist/block, простой пакет.</summary>
    private const uint HitInfoAffectsVictim = 0x2;

    private const byte VictimStateHit = 1;
    private const uint SchoolMaskPhysical = 1;

    [WorldOpcodeHandler(WorldOpcode.CmsgSetSelection)]
    public static Task OnSetSelection(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        session.SelectionGuid = reader.UInt64(); // plain Guid (не packed)
        return Task.CompletedTask;
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAttackSwing)]
    public static async Task OnAttackSwing(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var victimGuid = reader.UInt64(); // plain Guid

        var creature = session.World.FindCreature(victimGuid);
        if (creature is null || !creature.IsAlive)
            return; // нечего атаковать (нет сущности/уже труп)

        session.CombatTargetGuid = victimGuid;
        session.NextMeleeSwingMs = Environment.TickCount64; // первый свинг — на ближайшем тике
        session.MeleeNotInRangeNotified = false;

        var start = new ByteWriter(16)
            .UInt64(session.InWorldGuid)
            .UInt64(victimGuid);
        await session.SendAsync(WorldOpcode.SmsgAttackStart, start.ToArray(), ct);
        session.Logger.LogDebug("ATTACKSWING '{User}' → существо guid={Guid}", session.Account, victimGuid);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAttackStop)]
    public static async Task OnAttackStop(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var enemy = session.CombatTargetGuid;
        if (enemy != 0)
            await StopAttackAsync(session, enemy, ct);
    }

    /// <summary>
    /// Тик авто-атаки игрока: если пора свингать и цель жива — наносим урон, рассылаем
    /// SMSG_ATTACKERSTATEUPDATE + апдейт HP наблюдателям; на смерти — стоп и таймер респавна.
    /// </summary>
    internal static async Task TickMeleeAsync(WorldSession session, long now, CancellationToken ct)
    {
        var targetGuid = session.CombatTargetGuid;
        if (targetGuid == 0 || session.InWorldGuid == 0)
            return;

        var creature = session.World.FindCreature(targetGuid);
        if (creature is null || !creature.IsAlive)
        {
            await StopAttackAsync(session, targetGuid, ct);
            return;
        }

        if (now < session.NextMeleeSwingMs)
            return;

        // Мили-радиус: пока цель далеко — свинг «держим» (таймер не двигаем), клиенту один раз
        // шлём «слишком далеко». Подойдёт в радиус — удар пройдёт на ближайшем тике.
        if (!InMeleeRange(session, creature))
        {
            if (!session.MeleeNotInRangeNotified)
            {
                await session.SendAsync(WorldOpcode.SmsgAttackSwingNotInRange, [], ct);
                session.MeleeNotInRangeNotified = true;
            }
            return;
        }
        session.MeleeNotInRangeNotified = false;
        session.NextMeleeSwingMs = now + SwingIntervalMs;

        var damage = ComputeMeleeDamage(session.Character?.Level ?? 1);
        var (_, overkill, died) = session.World.ApplyCreatureDamage(creature, damage); // общий путь урона (M6.4)

        var attackerGuid = (ulong)session.InWorldGuid;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAttackerStateUpdate,
            BuildAttackerStateUpdate(attackerGuid, creature.Guid, damage, overkill), ct);
        await session.World.BroadcastCreatureHealthAsync(creature, ct);

        if (died)
        {
            await StopAttackAsync(session, creature.Guid, ct);
            session.Logger.LogInformation("KILL '{User}' убил '{Name}' (guid={Guid}), респавн через {Sec}с",
                session.Account, creature.Template.Name, creature.Guid, WorldState.RespawnDelay / 1000);
        }
    }

    private static async Task StopAttackAsync(WorldSession session, ulong enemyGuid, CancellationToken ct)
    {
        session.CombatTargetGuid = 0;
        var w = new ByteWriter(16);
        PackedGuid.Write(w, (ulong)session.InWorldGuid);
        PackedGuid.Write(w, enemyGuid);
        w.UInt32(0); // unknown1
        await session.SendAsync(WorldOpcode.SmsgAttackStop, w.ToArray(), ct);
    }

    /// <summary>В мили-радиусе ли игрок до существа (3D, центр-в-центр). M6.3.</summary>
    private static bool InMeleeRange(WorldSession session, WorldCreature creature)
    {
        var dx = session.PosX - creature.X;
        var dy = session.PosY - creature.Y;
        var dz = session.PosZ - creature.Z;
        return dx * dx + dy * dy + dz * dz <= MeleeRangeYards * MeleeRangeYards;
    }

    /// <summary>Мили-урон игрока (упрощённо по уровню; точные статы/оружие — позже).</summary>
    private static uint ComputeMeleeDamage(byte level)
    {
        var lvl = Math.Max((byte)1, level);
        var min = 8 + lvl;
        var max = 14 + lvl * 2;
        return (uint)Random.Shared.Next(min, max + 1);
    }

    /// <summary>SMSG_ATTACKERSTATEUPDATE (3.3.5a) — одна запись урона, без absorb/resist/block.</summary>
    private static byte[] BuildAttackerStateUpdate(ulong attacker, ulong target, uint damage, uint overkill)
    {
        var w = new ByteWriter(48);
        w.UInt32(HitInfoAffectsVictim);
        PackedGuid.Write(w, attacker);
        PackedGuid.Write(w, target);
        w.UInt32(damage);     // total_damage
        w.UInt32(overkill);   // overkill
        w.UInt8(1);           // amount_of_damages
        // DamageInfo[0]
        w.UInt32(SchoolMaskPhysical)
         .Single(damage)      // damage_float
         .UInt32(damage);     // damage_uint
        w.UInt8(VictimStateHit);
        w.UInt32(0);          // unknown1
        w.UInt32(0);          // unknown2
        return w.ToArray();
    }
}
