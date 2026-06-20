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
internal sealed class CreatureCombatAI(CombatResourcesService combatResources, AbsorbShieldService absorbShields,
    SpellCatalog spellCatalog, KillRewardService killReward, AuraStateService auraState, ProcService procs)
{
    /// <summary>SMSG_AI_REACTION: реакция «HOSTILE» (рык агро) при входе существа в бой.</summary>
    private const uint AiReactionHostile = 2;
    private const byte SchoolMaskPhysical = 1; // SCHOOL_MASK_NORMAL — школа мили-урона существ (ABS.1)
    private const byte VictimStateImmune = 7;  // VICTIMSTATE_IS_IMMUNE — клиент рисует «Иммунитет» (IMMUNITY.1)
    private const uint HolyShieldAura = 48952; // Щит небес (Holy Shield): при блоке — урон Светом по атакующему. BLOCK.2
    private const byte SchoolHoly = 2;         // SCHOOL_MASK_HOLY — школа урона Щита небес
    private const float CreatureCritChance = 5f; // % крита существа по игроку (упрощённо; ×2). CRIT.2
    private const uint CasterDummyCastMs = 2500;    // каст-тайм кастующего манекена (удобно ловить прерывание). INT.1
    private const long CasterDummyCastGapMs = 1500; // пауза между кастами кастующего манекена. INT.1
    private const long HunterShotGapMs = 1800;      // Ф2 #14: пауза между выстрелами манекена-охотника.

    // --- Преследование/возврат (M6.7 инкр.2) ---
    /// <summary>Скорость бега существа (ярды/с) — совпадает с RunSpeed в create-блоке существа.</summary>
    private const float CreatureRunSpeed = 7.0f;
    /// <summary>Кадэнс шага движения (мс) — троттлинг сплайнов SMSG_MONSTER_MOVE.</summary>
    private const long MoveIntervalMs = 500;
    /// <summary>Leash: если существо удалилось от дома дальше — выходит из боя и возвращается.</summary>
    private const float LeashRadius = 50f;
    /// <summary>Дистанция (ярды²) разрыва боя для атакующего манекена: стоит на месте, отошёл на 40 ярдов — evade.</summary>
    private const float AttackDummyLeashSq = 40f * 40f;
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

        // §4 Страх: пока существо в панике, leash не выводит из боя — иначе fleeing-побег за 50 ярдов сразу
        // обрывал бы страх (evade). После окончания страха leash сработает штатно (вернётся на спавн).
        var feared = creature.CrowdControl == SpellCatalog.CrowdControlKind.Fear
            && CrowdControlService.PreventsAction(creature, now);

        // Leash: ушли от дома слишком далеко (игрок «утащил») — выходим из боя и возвращаемся.
        var hx = creature.X - creature.HomeX;
        var hy = creature.Y - creature.HomeY;
        var hz = creature.Z - creature.HomeZ;
        if (!feared && hx * hx + hy * hy + hz * hz > LeashRadius * LeashRadius)
        {
            await BeginEvadeAsync(world, creature, ct);
            return;
        }

        // Фаза 2 CC: активный стан/страх/дезориентация — существо не атакует, оставаясь «в бою». Стан/
        // дезориентация — стоит на месте; СТРАХ — в панике бежит прочь от игрока (§4 fleeing). Рут/немота
        // свинг не блокируют (визуал на клиенте). Истечение CC снимается в WorldTick.
        if (CrowdControlService.PreventsAction(creature, now))
        {
            if (feared && now >= creature.NextMoveMs)
            {
                creature.NextMoveMs = now + MoveIntervalMs;
                await FleeFromAsync(world, creature, player.X, player.Y, player.Z, ct);
            }
            return;
        }

        // Фаза 2 INT.1: кастующий манекен — крутит каст-бар по игроку (стенд для проверки прерывания), не бьёт.
        if (Npcs.IsCasterDummy(creature.Template.Entry))
        {
            await TickCasterDummyAsync(world, creature, player, now, ct);
            return;
        }

        // Ф2 #14: манекен-охотник — стреляет по игроку на расстоянии (физ. урон), не подходит вплотную.
        if (Npcs.IsHunterDummy(creature.Template.Entry))
        {
            await TickHunterDummyAsync(world, creature, player, now, ct);
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

            // IMMUNITY.1: «пузырь» неуязвимости (Divine Shield/Ice Block/Hand of Protection) — активная аура
            // с маской, покрывающей школу удара (мили — физ.). Урон гасится в ноль, клиент рисует «Иммунитет»
            // (VictimState=7); HP/ярость/смерть не трогаем. Эталон — CMaNGOS Unit::IsImmuneToDamage.
            if (player.Session.Progression.Periodics.Any(p => p.TargetGuid == 0
                    && (p.ImmuneSchoolMask & SchoolMaskPhysical) != 0 && now < p.ExpiresAtMs))
            {
                await world.BroadcastToPlayerObserversAsync(player, WorldOpcode.SmsgAttackerStateUpdate,
                    CombatPackets.BuildAttackerStateUpdate(creature.Guid, player.Guid, 0, 0, VictimStateImmune), ct);
                return;
            }

            // Защита: уклонение/парирование (обход) + митигейшн (броня/блок/«Глухая оборона»). Блок считаем
            // вживую (класс + щит + ауры «Блок щитом») — надёжнее кэша; уклон/парри/броня — из кэша RefreshMelee.
            var raw = ComputeCreatureMeleeDamage(creature.Template.Level);
            var cs = player.Session.Combat;
            var pclass = player.Session.Character?.Class ?? 0;
            var periodics = player.Session.Progression.Periodics;
            var blockPct = CombatStats.BlockPercent(pclass, cs.HasShield,
                periodics.Where(p => p.TargetGuid == 0).Sum(p => p.BlockBonus));
            // % получаемого урона: из временных аур-эффектов (Глухая оборона/Shield Wall — Periodics) И из
            // перманентных аур-переключателей (§1 Frost Presence −9% — ActiveAura). Спелл живёт в одном из
            // путей, не в обоих, → двойного учёта нет.
            var dmgTaken = periodics.Where(p => p.TargetGuid == 0).Sum(p => p.DamageTakenPct)
                + player.Session.Progression.Auras.Sum(a => a.DamageTakenPct);
            // DODGE.1: базовый dodge (статы) + бонус от аур (Evasion рога) — avoidance до митигейшна.
            var dodgePct = cs.DodgePct + periodics.Where(p => p.TargetGuid == 0).Sum(p => p.DodgeBonus);
            // SPELL.T1: парри = база (статы) + аура-сумма (MOD_PARRY_PERCENT + MOD_RATING/CR_PARRY).
            var parryPct = cs.ParryPct + periodics.Where(p => p.TargetGuid == 0).Sum(p => p.ParryChancePct);
            var (damage, outcome, blocked) = CombatStats.ResolveIncomingMelee(
                raw, dodgePct, parryPct, blockPct, cs.ArmorValue, creature.Template.Level,
                dmgTaken, Random.Shared.NextDouble(), Random.Shared.NextDouble());

            // DEFENSE.1: успешный dodge/parry/block → 5-секундное окно AURA_STATE_DEFENSE на игроке.
            // Это разблокирует Revenge (CasterAuraState=1) и подсветит кнопку у клиента
            // (UNIT_FIELD_AURASTATE бит 0).
            if (outcome == CombatStats.MeleeOutcome.Dodge
                || outcome == CombatStats.MeleeOutcome.Parry
                || blocked > 0)
                await auraState.SetDefenseAsync(player.Session, now, ct);

            // DEFENSE.2: успешный parry у Hunter (class=3) → 5-секундное окно AURA_STATE_HUNTER_PARRY
            // (UNIT_FIELD_AURASTATE бит 6). Разблокирует Counterattack (CasterAuraState=7). Только parry —
            // не dodge/block. У других классов state=7 имеет другие триггеры (Warrior Victory Rush после
            // kill — отдельная итерация).
            if (outcome == CombatStats.MeleeOutcome.Parry && pclass == 3)
                await auraState.SetHunterParryAsync(player.Session, now, ct);

            // #3797 DK Rune Strike: успешный dodge/parry у DK (class=6) → 5-секундное серверное окно
            // (без клиентского AURA_STATE — Rune Strike не использует бит UNIT_FIELD_AURASTATE).
            // SpellCastService проверит RuneStrikeWindowExpiresMs для spellId 56815/56816.
            if (pclass == 6 && (outcome == CombatStats.MeleeOutcome.Dodge || outcome == CombatStats.MeleeOutcome.Parry))
                player.Session.Combat.RuneStrikeWindowExpiresMs = now + AuraStateService.DefenseStateDurationMs;
            // CRIT.2: крит существа по игроку (фикс. шанс) — только по прошедшему удару, ×2 + флаг (клиент рисует крит).
            var crit = outcome == CombatStats.MeleeOutcome.Hit && Random.Shared.NextDouble() * 100.0 < CreatureCritChance;
            if (crit)
                damage *= 2;
            // ABS.1: absorb-щиты гасят урон ПОСЛЕ митигейшна, до HP (мили существа — физическая школа, маска 1).
            var absorbed = await absorbShields.AbsorbAsync(player.Session, SchoolMaskPhysical, damage, ct);
            // ABS.3 Sacred Shield (53601): прок-поглощение текущего удара (до ~500), не чаще раз в 6 с.
            absorbed += await absorbShields.TrySacredShieldAsync(player.Session, damage - absorbed, now, ct);
            damage -= absorbed;
            var (_, died) = world.ApplyPlayerDamage(player, damage);
            player.Session.Combat.LastCombatMs = now; // M6.7: получил урон → пауза регена
            if (damage > 0)
                await combatResources.GainRageAsync(player.Session, damage, attacker: false, ct); // M6.12: ярость за полученный урон

            // PROC.T1/T2 (порт CMaNGOS UnitAuraProcHandler): victim-side проки на ауры игрока.
            // TakeMeleeSwing — авто-удар по нему; TakeAnyDamage — любой урон прошёл (после ABS/блока).
            // procEx из outcome: Hit/Crit + avoid-биты (Dodge/Parry/Block) — Natural Reaction (talent 57878,
            // rage on dodge), Improved Hamstring (proc on hit) и т.п. опираются на эти биты.
            var victimFlags = ProcFlag.TakeMeleeSwing;
            if (damage > 0)
                victimFlags |= ProcFlag.TakeAnyDamage;
            var victimEx = outcome switch
            {
                CombatStats.MeleeOutcome.Dodge => ProcFlagEx.Dodge,
                CombatStats.MeleeOutcome.Parry => ProcFlagEx.Parry,
                _ => crit ? ProcFlagEx.CriticalHit : ProcFlagEx.NormalHit,
            };
            if (blocked > 0)
                victimEx |= ProcFlagEx.Block;
            await procs.TryProcAsync(player.Session, victimFlags, ct,
                procEx: victimEx, spellSchoolMask: SchoolMaskPhysical,
                weaponAttackSpeedMs: (uint)PlayerMeleeService.SwingIntervalMs);

            await world.BroadcastToPlayerObserversAsync(player, WorldOpcode.SmsgAttackerStateUpdate,
                CombatPackets.BuildAttackerStateUpdate(creature.Guid, player.Guid, damage, 0, (byte)outcome, blocked, absorbed, crit), ct);
            await world.BroadcastPlayerHealthAsync(player, ct);

            // BLOCK.2 Щит небес (Holy Shield 48952): при успешном блоке — урон Светом по атакующему.
            if (blocked > 0 && creature.IsAlive
                && player.Session.Progression.Auras.Any(a => a.SpellId == HolyShieldAura))
                await HolyShieldReflectAsync(world, player, creature, now, ct);

            if (died)
                await HandlePlayerDeathAsync(world, player, creature, ct);
            return;
        }

        // Атакующий манекен (проверка защиты) стоит на месте — НЕ преследует. Отошёл на ~40 ярдов →
        // ДЕСПАВН (чисто исчезает, бой завершается; для нового теста — .dummy attack снова).
        if (Npcs.IsAttackDummy(creature.Template.Entry))
        {
            var px = player.X - creature.X;
            var py = player.Y - creature.Y;
            var pz = player.Z - creature.Z;
            if (px * px + py * py + pz * pz > AttackDummyLeashSq)
                await world.DespawnCreatureAsync(creature, ct);
            return;
        }

        // §4: рут (Ледяные оковы/Frost Nova) обездвиживает — существо не преследует (бить в упор может, выше).
        if (CrowdControlService.IsRooted(creature, now))
            return;

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

        // §8 снара (Crippling Poison): замедляем эффективную скорость бега (преследование и побег при страхе).
        var speed = CreatureRunSpeed * CrowdControlService.SnareSpeedFactor(creature, Environment.TickCount64);
        var stepLen = speed * MoveIntervalMs / 1000f;
        float nx, ny, nz;
        uint durationMs;
        if (dist <= stepLen)
        {
            nx = tx; ny = ty; nz = tz;
            durationMs = (uint)MathF.Max(1f, dist / speed * 1000f);
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

    /// <summary>
    /// §4 Страх (fleeing): шаг побега прочь от источника угрозы. Направление = от игрока к существу со
    /// случайным отклонением (паника бежит эрратично, перевыбор каждый тик). Цель — ~8 ярдов прочь;
    /// дальше работает <see cref="StepTowardAsync"/> (навмеш: обход препятствий + Z по земле).
    /// </summary>
    private static async Task FleeFromAsync(WorldState world, WorldCreature creature,
        float px, float py, float pz, CancellationToken ct)
    {
        const float FleeGoalDistance = 8f; // целевая точка побега впереди по направлению «прочь»
        var dx = creature.X - px;
        var dy = creature.Y - py;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        float dirX, dirY;
        if (dist < 1e-3f)
        {
            var a = (float)(Random.Shared.NextDouble() * 2 * Math.PI); // совпали по позиции — бежим в случайную сторону
            dirX = MathF.Cos(a);
            dirY = MathF.Sin(a);
        }
        else
        {
            dirX = dx / dist;
            dirY = dy / dist;
        }
        // Эрратичность страха: поворот направления на случайный угол ±~0.5 рад.
        var jitter = (float)((Random.Shared.NextDouble() - 0.5) * 1.0);
        var ca = MathF.Cos(jitter);
        var sa = MathF.Sin(jitter);
        var gx = creature.X + (dirX * ca - dirY * sa) * FleeGoalDistance;
        var gy = creature.Y + (dirX * sa + dirY * ca) * FleeGoalDistance;
        await StepTowardAsync(world, creature, gx, gy, creature.Z, ct);
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

    /// <summary>
    /// Фаза 2 INT.1: кастующий манекен — зацикленный каст-бар по игроку (SMSG_SPELL_START → SMSG_SPELL_GO).
    /// Урон не наносит (каст «вхолостую»); цель — стенд для проверки прерывания (Kick/Counterspell/Pummel).
    /// Прерывание (<see cref="Handlers.SpellCastCompletion"/>) сбрасывает каст и лочит школу — здесь ждём разлок.
    /// </summary>
    private async Task TickCasterDummyAsync(WorldState world, WorldCreature creature, WorldPlayer player, long now, CancellationToken ct)
    {
        // Идёт каст — завершаем по таймеру (вхолостую) и уходим на паузу.
        if (creature.CastingSpellId != 0)
        {
            if (now < creature.CastEndMs)
                return;
            await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgSpellGo,
                SpellPackets.BuildSpellGo(creature.Guid, creature.CastingSpellId, player.Guid, 0), ct);
            creature.CastingSpellId = 0;
            creature.NextCastMs = now + CasterDummyCastGapMs;
            return;
        }

        // Школа залочена прерыванием или ещё пауза — ждём.
        if (now < creature.SchoolLockUntilMs || now < creature.NextCastMs)
            return;

        // Начинаем новый каст по игроку: доворот + SMSG_SPELL_START (каст-бар у наблюдателей).
        creature.O = MathF.Atan2(player.Y - creature.Y, player.X - creature.X);
        creature.CastingSpellId = Npcs.CasterDummyCastSpellId;
        creature.CastSchoolMask = Npcs.CasterDummyCastSchoolMask;
        creature.CastEndMs = now + CasterDummyCastMs;
        await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgSpellStart,
            SpellPackets.BuildSpellStart(creature.Guid, Npcs.CasterDummyCastSpellId, 0, CasterDummyCastMs, player.Guid), ct);
    }

    /// <summary>Ф2 #14 Манекен-охотник: на кадансе наносит игроку физ. урон «выстрелом» (эмуляция дальнего боя).
    /// Урон — упрощённо по уровню (как мили существа); визуал — Auto Shot (SpellGo + лог урона). Тюнинг — в игре.</summary>
    private async Task TickHunterDummyAsync(WorldState world, WorldCreature creature, WorldPlayer player, long now, CancellationToken ct)
    {
        if (now < creature.NextCastMs)
            return;
        creature.NextCastMs = now + HunterShotGapMs;
        creature.O = MathF.Atan2(player.Y - creature.Y, player.X - creature.X);
        var dmg = ComputeCreatureMeleeDamage(creature.Template.Level);
        world.ApplyPlayerDamage(player, dmg);
        player.Session.Combat.LastCombatMs = now;
        await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgSpellGo,
            SpellPackets.BuildSpellGo(creature.Guid, Npcs.HunterShotSpellId, player.Guid, 0), ct);
        await world.BroadcastToPlayerObserversAsync(player, WorldOpcode.SmsgSpellNonMeleeDamageLog,
            SpellPackets.BuildDamageLog(creature.Guid, (ulong)player.Session.InWorldGuid, Npcs.HunterShotSpellId, dmg, 0, Npcs.SchoolPhysical), ct);
        await world.BroadcastPlayerHealthAsync(player, ct);
    }

    /// <summary>Ф2 #14 Лечебный манекен: самослив HP (кадэнс 1с) быстрее регена — хилер должен перелечивать.
    /// Не убиваем (минимум 1% макс.), чтобы цель оставалась валидной. Тикается из WorldTick (вне боя).</summary>
    internal async Task TickHealerDrainAsync(WorldState world, WorldCreature creature, long now, CancellationToken ct)
    {
        if (now < creature.NextRegenMs)
            return;
        creature.NextRegenMs = now + 1000;
        var floor = Math.Max(1u, creature.MaxHealth / 100);
        if (creature.Health <= floor)
            return;
        creature.Health = (uint)Math.Max(floor, (long)creature.Health - Npcs.HealerDrainPerSec);
        await world.BroadcastCreatureHealthAsync(creature, ct);
    }

    /// <summary>BLOCK.2 Щит небес (Holy Shield): при блоке — урон Светом по атакующему существу
    /// (величина = aura 43 спелла 48952, ~274). Лог как спелл-урон; добивание учитывает награду.
    /// Todo: 8 зарядов (сейчас бьёт всё время действия баффа), поглощение блоком отдельно.</summary>
    private async Task HolyShieldReflectAsync(WorldState world, WorldPlayer player, WorldCreature creature, long now, CancellationToken ct)
    {
        var info = await spellCatalog.GetAsync(HolyShieldAura, ct);
        var amount = (uint)Math.Max(0, info?.BlockReflectDamage ?? 0);
        if (amount == 0)
            return;
        var (_, overkill, cdied) = world.ApplyCreatureDamage(creature, amount);
        player.Session.Combat.LastCombatMs = now;
        await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgSpellNonMeleeDamageLog,
            SpellPackets.BuildDamageLog(creature.Guid, (ulong)player.Session.InWorldGuid, HolyShieldAura, amount, overkill, SchoolHoly), ct);
        await world.BroadcastCreatureHealthAsync(creature, ct);
        if (cdied)
            await killReward.OnCreatureKilledAsync(player.Session, creature, ct);
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
