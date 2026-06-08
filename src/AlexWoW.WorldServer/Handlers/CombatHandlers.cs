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
        if (session.IsDead)
            return; // мёртвый не атакует (M6.7)

        var reader = packet.Reader();
        var victimGuid = reader.UInt64(); // plain Guid

        var creature = session.World.FindCreature(victimGuid);
        if (creature is null || !creature.IsAlive)
            return; // нечего атаковать (нет сущности/уже труп)

        session.CombatTargetGuid = victimGuid;
        session.NextMeleeSwingMs = Environment.TickCount64; // первый свинг — на ближайшем тике
        session.MeleeNotInRangeNotified = false;

        var start = BuildAttackStart((ulong)session.InWorldGuid, victimGuid);
        await session.SendAsync(WorldOpcode.SmsgAttackStart, start, ct);
        session.Logger.LogDebug("ATTACKSWING '{User}' → существо guid={Guid}", session.Account, victimGuid);

        // M6.7: существо отвечает — входит в бой с атакующим (рык агро при первом входе).
        await EnsureCreatureRetaliationAsync(session, creature, roar: true, ct);
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

        if (session.IsDead) // M6.7: мёртвый не свингает
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
        session.LastCombatMs = now; // M6.7: бой → пауза внебоевого регена HP

        var damage = ComputeMeleeDamage(session.Character?.Level ?? 1);
        var (_, overkill, died) = session.World.ApplyCreatureDamage(creature, damage); // общий путь урона (M6.4)
        await CombatResources.GainRageAsync(session, damage, attacker: true, ct); // M6.12: ярость за удар

        var attackerGuid = (ulong)session.InWorldGuid;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAttackerStateUpdate,
            BuildAttackerStateUpdate(attackerGuid, creature.Guid, damage, overkill), ct);
        await session.World.BroadcastCreatureHealthAsync(creature, ct);

        if (died)
        {
            await StopAttackAsync(session, creature.Guid, ct);
            await LootHandlers.OnCreatureKilledAsync(session, creature, ct); // M6.6: ролл лута + lootable-флаг
            session.Logger.LogInformation("KILL '{User}' убил '{Name}' (guid={Guid}), респавн через {Sec}с",
                session.Account, creature.Template.Name, creature.Guid, WorldState.RespawnDelay / 1000);
            return;
        }

        // M6.7: пока бьём существо — держим его в ответном бою (если оно сбросилось, напр. отходом).
        await EnsureCreatureRetaliationAsync(session, creature, roar: false, ct);
    }

    private static async Task StopAttackAsync(WorldSession session, ulong enemyGuid, CancellationToken ct)
    {
        session.CombatTargetGuid = 0;
        await session.SendAsync(WorldOpcode.SmsgAttackStop,
            BuildAttackStop((ulong)session.InWorldGuid, enemyGuid), ct);
    }

    // ===================== ИИ существ: ответный бой (M6.7 инкр.1) =====================

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

    /// <summary>
    /// Существо входит/остаётся в ответном бою с этим игроком. Идемпотентно: если уже в бою — no-op.
    /// <paramref name="roar"/> — слать ли рык агро (первый вход; на «удержании» — нет). M6.7.
    /// </summary>
    private static Task EnsureCreatureRetaliationAsync(WorldSession session, WorldCreature creature, bool roar, CancellationToken ct)
        => EnterCreatureCombatAsync(session.World, creature, (ulong)session.InWorldGuid, roar, ct);

    /// <summary>
    /// Существо входит в бой с целью (ответка M6.7 инкр.1 или авто-агро инкр.2b): сброс evade,
    /// рык агро (опц.) + ATTACKSTART наблюдателям, шаг — сразу. Идемпотентно (уже в бою → no-op).
    /// </summary>
    private static async Task EnterCreatureCombatAsync(WorldState world, WorldCreature creature, ulong targetGuid, bool roar, CancellationToken ct)
    {
        if (creature.CombatTargetGuid != 0)
            return;
        creature.CombatTargetGuid = targetGuid;
        creature.Evading = false;
        creature.NextSwingMs = Environment.TickCount64 + SwingIntervalMs;
        creature.NextMoveMs = 0;
        if (roar)
            await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAiReaction,
                BuildAiReaction(creature.Guid, AiReactionHostile), ct);
        await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAttackStart,
            BuildAttackStart(creature.Guid, targetGuid), ct);
    }

    /// <summary>Базовый радиус агро (ярды) враждебного существа к подошедшему игроку. M6.7 инкр.2b.</summary>
    private const float AggroRadius = 18f;

    /// <summary>
    /// Скан авто-агро (M6.7 инкр.2b): враждебное по фракции существо рядом с живым игроком и не в бою —
    /// само входит в бой (рык + преследование). Идём по видимым существам игрока (набор ограничен).
    /// </summary>
    internal static async Task TickAggroScanAsync(WorldState world, WorldPlayer player, long now, CancellationToken ct)
    {
        var session = player.Session;
        if (session.IsDead || session.InWorldGuid == 0)
            return;
        var playerFt = DisplayData.FactionForRace(player.Character.Race);

        foreach (var creature in session.VisibleNpcs.Values)
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
    internal static async Task TickCreatureCombatAsync(WorldState world, WorldCreature creature, long now, CancellationToken ct)
    {
        var player = world.FindPlayer(creature.CombatTargetGuid);
        // Цель пропала/вне мира/мертва/на другой карте — возврат на спавн (evade + реген).
        if (player is null || player.Session.InWorldGuid == 0 || player.Session.IsDead || player.Map != creature.Map)
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
            creature.NextSwingMs = now + SwingIntervalMs;

            var damage = ComputeCreatureMeleeDamage(creature.Template.Level);
            var (_, died) = world.ApplyPlayerDamage(player, damage);
            player.Session.LastCombatMs = now; // M6.7: получил урон → пауза регена
            await CombatResources.GainRageAsync(player.Session, damage, attacker: false, ct); // M6.12: ярость за полученный урон

            await world.BroadcastToPlayerObserversAsync(player, WorldOpcode.SmsgAttackerStateUpdate,
                BuildAttackerStateUpdate(creature.Guid, player.Guid, damage, 0), ct);
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
    internal static async Task TickEvadeAsync(WorldState world, WorldCreature creature, long now, CancellationToken ct)
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

    /// <summary>Задержка после боя до старта внебоевого регена HP игрока (мс).</summary>
    private const long OutOfCombatRegenDelayMs = 5000;
    /// <summary>Кадэнс регена HP игрока (мс).</summary>
    private const long HealthRegenIntervalMs = 1000;

    /// <summary>
    /// Внебоевой реген HP игрока (M6.7): спустя 5 с после последней боевой активности восстанавливает
    /// ~10% макс. HP в секунду до полного. Зовётся из серверного тика (рядом с реген-маны M6.4).
    /// </summary>
    internal static async Task TickPlayerRegenAsync(WorldSession session, long now, CancellationToken ct)
    {
        if (session.InWorldGuid == 0 || session.IsDead || session.Health >= session.MaxHealth)
            return;
        if (now - session.LastCombatMs < OutOfCombatRegenDelayMs)       // ещё «в бою» — реген на паузе
            return;
        if (now - session.LastHealthRegenMs < HealthRegenIntervalMs)
            return;

        session.LastHealthRegenMs = now;
        session.Health = Math.Min(session.MaxHealth, session.Health + Math.Max(1u, session.MaxHealth / 10));
        if (session.Player is { } player)
            await session.World.BroadcastPlayerHealthAsync(player, ct);
    }

    /// <summary>Реген HP существа вне боя (если повреждено и стоит дома). Низкий кадэнс. M6.7.</summary>
    internal static async Task TickRegenAsync(WorldState world, WorldCreature creature, long now, CancellationToken ct)
    {
        if (creature.Health >= creature.MaxHealth || now < creature.NextRegenMs)
            return;
        creature.NextRegenMs = now + 1000;
        creature.Health = Math.Min(creature.MaxHealth, creature.Health + Math.Max(1, creature.MaxHealth / 20));
        await world.BroadcastCreatureHealthAsync(creature, ct);
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
            await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgAttackStop,
                BuildAttackStop(creature.Guid, target), ct);
    }

    /// <summary>Смерть игрока: помечаем мёртвым, существо уходит в evade, игрок прекращает атаку. M6.7.</summary>
    private static async Task HandlePlayerDeathAsync(WorldState world, WorldPlayer player, WorldCreature killer, CancellationToken ct)
    {
        var session = player.Session;
        session.IsDead = true;
        await BeginEvadeAsync(world, killer, ct);                  // существо возвращается на спавн
        if (session.CombatTargetGuid != 0)                         // игрок прекращает свою атаку
            await StopAttackAsync(session, session.CombatTargetGuid, ct);
        await session.SendAsync(WorldOpcode.SmsgForcedDeathUpdate, [], ct); // сброс таймера release
        session.Logger.LogInformation("DEATH '{User}' убит существом '{Name}'", session.Account, killer.Template.Name);
    }

    /// <summary>
    /// CMSG_REPOP_REQUEST («отпустить дух»): возрождаем на месте с полным HP (упрощённо — без
    /// кладбища/бега к трупу; corpse-run добавим позже). M6.7 инкр.1.
    /// </summary>
    [WorldOpcodeHandler(WorldOpcode.CmsgRepopRequest)]
    public static async Task OnRepopRequest(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        if (!session.IsDead || session.Player is not { } player)
            return;
        session.IsDead = false;
        session.Health = session.MaxHealth;
        await session.World.BroadcastPlayerHealthAsync(player, ct);
        session.Logger.LogInformation("RESPAWN '{User}' возродился на месте (полное HP)", session.Account);
    }

    /// <summary>В мили-радиусе ли существо до игрока (3D, центр-в-центр). M6.7.</summary>
    private static bool InMeleeRangeOfCreature(WorldCreature creature, WorldPlayer player)
    {
        var dx = creature.X - player.X;
        var dy = creature.Y - player.Y;
        var dz = creature.Z - player.Z;
        return dx * dx + dy * dy + dz * dz <= MeleeRangeYards * MeleeRangeYards;
    }

    /// <summary>Мили-урон существа (упрощённо по уровню; точные статы — позже). Чуть мягче игрока.</summary>
    private static uint ComputeCreatureMeleeDamage(byte level)
    {
        var lvl = Math.Max((byte)1, level);
        var min = 8 + lvl * 2;
        var max = 14 + lvl * 3;
        return (uint)Random.Shared.Next(min, max + 1);
    }

    /// <summary>SMSG_ATTACKSTART (3.3.5): plain u64 attacker + u64 victim.</summary>
    private static byte[] BuildAttackStart(ulong attacker, ulong victim)
        => new ByteWriter(16).UInt64(attacker).UInt64(victim).ToArray();

    /// <summary>SMSG_ATTACKSTOP (3.3.5): packed attacker + packed victim + u32 0.</summary>
    private static byte[] BuildAttackStop(ulong attacker, ulong victim)
    {
        var w = new ByteWriter(16);
        PackedGuid.Write(w, attacker);
        PackedGuid.Write(w, victim);
        w.UInt32(0);
        return w.ToArray();
    }

    /// <summary>SMSG_AI_REACTION (3.3.5): plain u64 guid + u32 reaction.</summary>
    private static byte[] BuildAiReaction(ulong guid, uint reaction)
        => new ByteWriter(12).UInt64(guid).UInt32(reaction).ToArray();

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
