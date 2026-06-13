using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Авто-атака игрока (M6.3, DI-сервис M7 S4 — вынос из god-класса CombatHandlers): старт/стоп атаки,
/// тик мили-свинга из серверного тика (<see cref="World.WorldTick.UpdateAsync"/>), расчёт урона игрока.
/// Сторона существа (ответка/агро/evade) — <see cref="CreatureCombatAI"/>, реген HP —
/// <see cref="RegenService"/>, байты пакетов — <see cref="Protocol.CombatPackets"/>.
/// </summary>
internal sealed class PlayerMeleeService(
    CreatureCombatAI creatureAi,
    CombatResourcesService combatResources,
    KillRewardService killReward,
    SealService seals,
    ProcService procs)
{
    /// <summary>Интервал мили-свинга (мс). Упрощённо — без учёта скорости оружия (точнее в M6.4+).</summary>
    internal const long SwingIntervalMs = 2000;

    /// <summary>
    /// Мили-радиус (ярды) центр-в-центр. ≈ NOMINAL_MELEE_RANGE: combat reach игрока (~1.5) +
    /// существа (~1.5) + запас. Упрощённо плоской константой (точная формула с combat reach — позже).
    /// </summary>
    internal const float MeleeRangeYards = 5.0f;

    /// <summary>
    /// Старт авто-атаки по существу (CMSG_ATTACKSWING): валидация цели, взвод свинг-таймера,
    /// SMSG_ATTACKSTART. Первый удар нанесёт ближайший серверный тик.
    /// </summary>
    internal async Task StartAttackAsync(WorldSession session, ulong victimGuid, CancellationToken ct)
    {
        if (session.Combat.IsDead)
            return; // мёртвый не атакует (M6.7)

        var creature = session.World.FindCreature(victimGuid);
        if (creature is null || !creature.IsAlive)
            return; // нечего атаковать (нет сущности/уже труп)

        session.Combat.CombatTargetGuid = victimGuid;
        session.Combat.NextMeleeSwingMs = Environment.TickCount64; // первый свинг — на ближайшем тике
        session.Combat.MeleeNotInRangeNotified = false;

        var start = CombatPackets.BuildAttackStart((ulong)session.InWorldGuid, victimGuid);
        await session.SendAsync(WorldOpcode.SmsgAttackStart, start, ct);
        session.Logger.LogDebug("ATTACKSWING '{User}' → существо guid={Guid}", session.Account, victimGuid);

        // M7 #13: НЕ агрим существо по самой команде атаки (она приходит с любой дистанции) — нейтрал
        // становится враждебным только по реально нанесённому удару (см. TickMeleeAsync). Враждебные
        // мобы агрятся независимо через авто-агро по близости (M6.7-2b).
    }

    /// <summary>
    /// Тик авто-атаки игрока: если пора свингать и цель жива — наносим урон, рассылаем
    /// SMSG_ATTACKERSTATEUPDATE + апдейт HP наблюдателям; на смерти — стоп и таймер респавна.
    /// </summary>
    internal async Task TickMeleeAsync(WorldSession session, long now, CancellationToken ct)
    {
        var targetGuid = session.Combat.CombatTargetGuid;
        if (targetGuid == 0 || session.InWorldGuid == 0)
            return;

        if (session.Combat.IsDead) // M6.7: мёртвый не свингает
        {
            await StopAttackAsync(session, targetGuid, ct);
            return;
        }

        var creature = session.World.FindCreature(targetGuid);
        if (creature is null || !creature.IsAlive)
        {
            await StopAttackAsync(session, targetGuid, ct);
            return;
        }

        if (now < session.Combat.NextMeleeSwingMs)
            return;

        // Мили-радиус: пока цель далеко — свинг «держим» (таймер не двигаем), клиенту один раз
        // шлём «слишком далеко». Подойдёт в радиус — удар пройдёт на ближайшем тике.
        if (!InMeleeRange(session, creature))
        {
            if (!session.Combat.MeleeNotInRangeNotified)
            {
                await session.SendAsync(WorldOpcode.SmsgAttackSwingNotInRange, [], ct);
                session.Combat.MeleeNotInRangeNotified = true;
            }
            return;
        }
        session.Combat.MeleeNotInRangeNotified = false;
        session.Combat.NextMeleeSwingMs = now + SwingIntervalMs;
        session.Combat.LastCombatMs = now; // M6.7: бой → пауза внебоевого регена HP

        var damage = ComputeMeleeDamage(session.Character?.Level ?? 1);
        var (_, overkill, died) = session.World.ApplyCreatureDamage(creature, damage); // общий путь урона (M6.4)
        await combatResources.GainRageAsync(session, damage, attacker: true, ct); // M6.12: ярость за удар

        var attackerGuid = (ulong)session.InWorldGuid;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAttackerStateUpdate,
            CombatPackets.BuildAttackerStateUpdate(attackerGuid, creature.Guid, damage, overkill), ct);
        await session.World.BroadcastCreatureHealthAsync(creature, ct);

        if (died)
        {
            await StopAttackAsync(session, creature.Guid, ct);
            await killReward.OnCreatureKilledAsync(session, creature, ct); // M6.6: ролл лута + lootable-флаг
            session.Logger.LogInformation("KILL '{User}' убил '{Name}' (guid={Guid}), респавн через {Sec}с",
                session.Account, creature.Template.Name, creature.Guid, WorldState.RespawnDelay / 1000);
            return;
        }

        // Фаза 2 PROC.1: проки на успешный мили-свинг (Sudden Death и т.п.). Школа удара — физическая (1),
        // чтобы прок с SchoolMask (напр. Omen of Clarity, маска 127) проходил фильтр школы (PROC.2). Мили-крита нет → wasCrit:false.
        await procs.TryProcAsync(session, ProcService.ProcFlagDealMeleeSwing, ct, wasCrit: false, spellSchoolMask: 1);

        // Фаза 2: on-hit прок активной печати паладина (holy-урон / хил / мана). Может добить цель.
        if (await seals.OnMeleeHitAsync(session, creature, now, ct))
        {
            await StopAttackAsync(session, creature.Guid, ct);
            return;
        }

        // M6.7/M7 #13: ответный бой запускается по landed-удару (в мили-радиусе). Рык агро — при первом
        // входе в бой (EnterCreatureCombatAsync идемпотентен: если уже в бою, roar игнорируется).
        await creatureAi.EnsureCreatureRetaliationAsync(session, creature, roar: true, ct);
    }

    /// <summary>Прекращает авто-атаку игрока: сброс цели + SMSG_ATTACKSTOP.</summary>
    internal async Task StopAttackAsync(WorldSession session, ulong enemyGuid, CancellationToken ct)
    {
        session.Combat.CombatTargetGuid = 0;
        await session.SendAsync(WorldOpcode.SmsgAttackStop,
            CombatPackets.BuildAttackStop((ulong)session.InWorldGuid, enemyGuid), ct);
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
}
