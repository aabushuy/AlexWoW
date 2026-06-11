using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// ИИ существ в бою (M6.7, DI-сервис M7 S4 — вынос из god-класса CombatHandlers): вход в ответный бой,
/// авто-агро по фракции, тик боя (удары/преследование/leash) и возврат на спавн (evade).
/// Существо — авторитетная <see cref="WorldCreature"/> из реестра мира (общее HP для наблюдателей).
/// Мили игрока — <see cref="PlayerMeleeService"/>, байты пакетов — <see cref="Protocol.CombatPackets"/>.
/// </summary>
internal sealed class CreatureCombatAI(CombatResourcesService combatResources)
{
    /// <summary>SMSG_AI_REACTION: реакция «HOSTILE» (рык агро) при входе существа в бой.</summary>
    private const uint AiReactionHostile = 2;

    // --- Преследование/возврат (M6.7 инкр.2) ---
    /// <summary>Скорость бега существа (ярды/с) — совпадает с RunSpeed в create-блоке существа.</summary>
    private const float CreatureRunSpeed = 7.0f;
    /// <summary>Кадэнс шага движения (мс) — троттлинг сплайнов SMSG_MONSTER_MOVE.</summary>
    private const long MoveIntervalMs = 500;
    /// <summary>Leash: если существо удалилось от дома дальше — выходит из боя и возвращается.</summary>
    private const float LeashRadius = 50f;
    /// <summary>Прибыл домой (ярды²) — порог завершения возврата (evade).</summary>
    private const float HomeArriveSq = 1.0f;

    /// <summary>Базовый радиус агро (ярды) враждебного существа к подошедшему игроку. M6.7 инкр.2b.</summary>
    private const float AggroRadius = 18f;

    /// <summary>
    /// Существо входит/остаётся в ответном бою с этим игроком. Идемпотентно: если уже в бою — no-op.
    /// <paramref name="roar"/> — слать ли рык агро (первый вход; на «удержании» — нет). M6.7.
    /// </summary>
    internal Task EnsureCreatureRetaliationAsync(WorldSession session, WorldCreature creature, bool roar, CancellationToken ct)
        => EnterCreatureCombatAsync(session.World, creature, (ulong)session.InWorldGuid, roar, ct);

    /// <summary>
    /// Существо входит в бой с целью (ответка M6.7 инкр.1 или авто-агро инкр.2b): сброс evade,
    /// рык агро (опц.) + ATTACKSTART наблюдателям, шаг — сразу. Идемпотентно (уже в бою → no-op).
    /// </summary>
    private static async Task EnterCreatureCombatAsync(WorldState world, WorldCreature creature, ulong targetGuid, bool roar, CancellationToken ct)
    {
        // #28/M12: тестовые манекены (урон/хил) пассивны — никогда не входят в бой (ни ответка, ни авто-агро).
        if (Protocol.Npcs.IsTestDummy(creature.Template.Entry))
            return;
        if (creature.CombatTargetGuid != 0)
            return;
        creature.CombatTargetGuid = targetGuid;
        creature.Evading = false;
        creature.NextSwingMs = Environment.TickCount64 + PlayerMeleeService.SwingIntervalMs;
        creature.NextMoveMs = 0;
        if (roar)
        {
            await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAiReaction,
                CombatPackets.BuildAiReaction(creature.Guid, AiReactionHostile), ct);
        }

        await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAttackStart,
            CombatPackets.BuildAttackStart(creature.Guid, targetGuid), ct);
    }

    /// <summary>
    /// Скан авто-агро (M6.7 инкр.2b): враждебное по фракции существо рядом с живым игроком и не в бою —
    /// само входит в бой (рык + преследование). Идём по видимым существам игрока (набор ограничен).
    /// </summary>
    internal async Task TickAggroScanAsync(WorldState world, WorldPlayer player, long now, CancellationToken ct)
    {
        var session = player.Session;
        if (session.Combat.IsDead || session.InWorldGuid == 0)
            return;
        var playerFt = DisplayData.FactionForRace(player.Character.Race);

        foreach (var creature in session.Visibility.VisibleNpcs.Values)
        {
            if (!creature.IsAlive || creature.CombatTargetGuid != 0 || creature.Evading)
                continue;
            if (!InAggroRange(creature, player))
                continue;
            if (!world.IsHostile(creature.Template.Faction, playerFt))
                continue;
            await EnterCreatureCombatAsync(world, creature, player.Guid, roar: true, ct);
        }
    }

    private static bool InAggroRange(WorldCreature creature, WorldPlayer player)
    {
        var dx = creature.X - player.X;
        var dy = creature.Y - player.Y;
        var dz = creature.Z - player.Z;
        return dx * dx + dy * dy + dz * dz <= AggroRadius * AggroRadius;
    }

    /// <summary>
    /// Тик ответного боя существа: бьёт игрока-цель в мили-радиусе. Цель пропала/мертва/отошла —
    /// существо выходит из боя (evade). Преследование по навмешу — инкремент 2.
    /// </summary>
    internal async Task TickCreatureCombatAsync(WorldState world, WorldCreature creature, long now, CancellationToken ct)
    {
        var player = world.FindPlayer(creature.CombatTargetGuid);
        // Цель пропала/вне мира/мертва/на другой карте — возврат на спавн (evade + реген).
        if (player is null || player.Session.InWorldGuid == 0 || player.Session.Combat.IsDead || player.Map != creature.Map)
        {
            await BeginEvadeAsync(world, creature, ct);
            return;
        }

        // Leash: ушли от дома слишком далеко (игрок «утащил») — выходим из боя и возвращаемся.
        var hx = creature.X - creature.HomeX;
        var hy = creature.Y - creature.HomeY;
        var hz = creature.Z - creature.HomeZ;
        if (hx * hx + hy * hy + hz * hz > LeashRadius * LeashRadius)
        {
            await BeginEvadeAsync(world, creature, ct);
            return;
        }

        // В мили-радиусе — бьём; иначе преследуем по навмешу (троттлинг шагов).
        if (InMeleeRangeOfCreature(creature, player))
        {
            // Доворот к игроку при страфе (троттлинг + порог изменения угла) — иначе моб стоит «боком».
            var desiredO = MathF.Atan2(player.Y - creature.Y, player.X - creature.X);
            if (now >= creature.NextFaceMs
                && MathF.Abs((float)Math.IEEERemainder(desiredO - creature.O, 2 * Math.PI)) > 0.2f)
            {
                creature.NextFaceMs = now + 400;
                creature.O = desiredO;
                await world.FaceCreatureAsync(creature, player.Guid, ct);
            }

            if (now < creature.NextSwingMs)
                return;
            creature.NextSwingMs = now + PlayerMeleeService.SwingIntervalMs;

            var damage = ComputeCreatureMeleeDamage(creature.Template.Level);
            var (_, died) = world.ApplyPlayerDamage(player, damage);
            player.Session.Combat.LastCombatMs = now; // M6.7: получил урон → пауза регена
            await combatResources.GainRageAsync(player.Session, damage, attacker: false, ct); // M6.12: ярость за полученный урон

            await world.BroadcastToPlayerObserversAsync(player, WorldOpcode.SmsgAttackerStateUpdate,
                CombatPackets.BuildAttackerStateUpdate(creature.Guid, player.Guid, damage, 0), ct);
            await world.BroadcastPlayerHealthAsync(player, ct);

            if (died)
                await HandlePlayerDeathAsync(world, player, creature, ct);
            return;
        }

        if (now < creature.NextMoveMs)
            return;
        creature.NextMoveMs = now + MoveIntervalMs;
        await StepTowardAsync(world, creature, player.X, player.Y, player.Z, ct);
    }

    /// <summary>Тик возврата на спавн (evade): шагаем домой, по прибытии — полный реген HP. M6.7.</summary>
    internal async Task TickEvadeAsync(WorldState world, WorldCreature creature, long now, CancellationToken ct)
    {
        var dx = creature.HomeX - creature.X;
        var dy = creature.HomeY - creature.Y;
        var dz = creature.HomeZ - creature.Z;
        if (dx * dx + dy * dy + dz * dz <= HomeArriveSq)
        {
            creature.Evading = false;
            creature.X = creature.HomeX;
            creature.Y = creature.HomeY;
            creature.Z = creature.HomeZ;
            creature.O = creature.HomeO;
            if (creature.Health < creature.MaxHealth)
            {
                creature.Health = creature.MaxHealth; // вернулся на спавн — полное здоровье
                await world.BroadcastCreatureHealthAsync(creature, ct);
            }
            return;
        }

        if (now < creature.NextMoveMs)
            return;
        creature.NextMoveMs = now + MoveIntervalMs;
        await StepTowardAsync(world, creature, creature.HomeX, creature.HomeY, creature.HomeZ, ct);
    }

    /// <summary>
    /// Один шаг движения к цели (мс-кадэнс): направление берём по навмешу (первая точка пути — обход
    /// препятствий), длина шага = скорость × интервал. Двигаем существо (SMSG_MONSTER_MOVE + позиция).
    /// </summary>
    private static async Task StepTowardAsync(WorldState world, WorldCreature creature,
        float gx, float gy, float gz, CancellationToken ct)
    {
        float tx = gx, ty = gy, tz = gz;
        var path = world.FindGroundPath(creature.Map, creature.X, creature.Y, creature.Z, gx, gy, gz);
        if (path is { Count: >= 2 })
            (tx, ty, tz) = path[1]; // path[0] — текущая точка; идём к следующей вершине пути

        var dx = tx - creature.X;
        var dy = ty - creature.Y;
        var dz = tz - creature.Z;
        var dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (dist < 1e-3f)
            return;

        var stepLen = CreatureRunSpeed * MoveIntervalMs / 1000f;
        float nx, ny, nz;
        uint durationMs;
        if (dist <= stepLen)
        {
            nx = tx; ny = ty; nz = tz;
            durationMs = (uint)MathF.Max(1f, dist / CreatureRunSpeed * 1000f);
        }
        else
        {
            var f = stepLen / dist;
            nx = creature.X + dx * f;
            ny = creature.Y + dy * f;
            nz = creature.Z + dz * f;
            durationMs = (uint)MoveIntervalMs;
        }
        await world.MoveCreatureAsync(creature, nx, ny, nz, durationMs, ct);
    }

    /// <summary>Существо выходит из боя и переходит в возврат на спавн (evade): ATTACKSTOP + флаг. M6.7.</summary>
    private static async Task BeginEvadeAsync(WorldState world, WorldCreature creature, CancellationToken ct)
    {
        var target = creature.CombatTargetGuid;
        creature.CombatTargetGuid = 0;
        creature.Evading = true;
        creature.NextMoveMs = 0; // шагнуть сразу
        if (target != 0)
        {
            await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAttackStop,
                CombatPackets.BuildAttackStop(creature.Guid, target), ct);
        }
    }

    /// <summary>Смерть игрока: помечаем мёртвым, существо уходит в evade, игрок прекращает атаку. M6.7.</summary>
    private static async Task HandlePlayerDeathAsync(WorldState world, WorldPlayer player, WorldCreature killer, CancellationToken ct)
    {
        var session = player.Session;
        session.Combat.IsDead = true;
        await BeginEvadeAsync(world, killer, ct);                  // существо возвращается на спавн
        if (session.Combat.CombatTargetGuid != 0)                         // игрок прекращает свою атаку
        {
            // Стоп-атака инлайном (та же последовательность, что PlayerMeleeService.StopAttackAsync):
            // ссылка на сервис мили создала бы цикл DI (мили → ИИ → мили). M7 S4.
            var enemy = session.Combat.CombatTargetGuid;
            session.Combat.CombatTargetGuid = 0;
            await session.SendAsync(WorldOpcode.SmsgAttackStop,
                CombatPackets.BuildAttackStop((ulong)session.InWorldGuid, enemy), ct);
        }
        await session.SendAsync(WorldOpcode.SmsgForcedDeathUpdate, [], ct); // сброс таймера release
        session.Logger.LogInformation("DEATH '{User}' убит существом '{Name}'", session.Account, killer.Template.Name);
    }

    /// <summary>В мили-радиусе ли существо до игрока (3D, центр-в-центр). M6.7.</summary>
    private static bool InMeleeRangeOfCreature(WorldCreature creature, WorldPlayer player)
    {
        var dx = creature.X - player.X;
        var dy = creature.Y - player.Y;
        var dz = creature.Z - player.Z;
        return dx * dx + dy * dy + dz * dz <= PlayerMeleeService.MeleeRangeYards * PlayerMeleeService.MeleeRangeYards;
    }

    /// <summary>Мили-урон существа (упрощённо по уровню; точные статы — позже). Чуть мягче игрока.</summary>
    private static uint ComputeCreatureMeleeDamage(byte level)
    {
        var lvl = Math.Max((byte)1, level);
        var min = 8 + lvl * 2;
        var max = 14 + lvl * 3;
        return (uint)Random.Shared.Next(min, max + 1);
    }
}
