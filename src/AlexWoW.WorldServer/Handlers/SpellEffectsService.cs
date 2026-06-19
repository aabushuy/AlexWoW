using AlexWoW.DataStores.Terrain;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Применение эффекта завершённого каста (M6.4, DI-сервис M7 S3 — бывший статик SpellEffects): прямой урон
/// по существу или хил игрока, движущие эффекты (рывок/телепорт, M7 #33). Отделено от оркестрации каста
/// (<see cref="SpellCastCompletion"/>) по SRP. Урон идёт общим путём с мили
/// (<see cref="World.WorldState.ApplyCreatureDamage"/>); хил опирается на авторитетное HP (M6.7).
/// </summary>
internal sealed class SpellEffectsService(
    CreatureCombatAI creatureAi,
    KillRewardService killReward,
    SpellTestCaptureService spellTestCapture,
    ProcService procs,
    CrowdControlService crowdControl,
    TerrainMaps terrain)
{
    /// <summary>Прямой урон спеллом по существу-цели: лог урона + HP наблюдателям + смерть/лут + ответный бой.</summary>
    internal async Task ApplyDamageAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetGuid, long now, CancellationToken ct, byte comboPoints = 0)
    {
        var creature = targetGuid != 0 ? session.World.FindCreature(targetGuid) : null;
        if (creature is null || !creature.IsAlive)
            return; // цель пропала/мертва — спелл «впустую»

        session.Combat.LastCombatMs = now; // M6.7: урон спеллом — пауза внебоевого регена HP
        var damage = ComputeDamage(session, info, comboPoints);
        // CRIT.1: спелл-крит — ролл шанса, на крит урон ×1.5 (множитель CMaNGOS), флаг в лог (клиент рисует крит).
        var crit = RollSpellCrit(session);
        if (crit)
            damage = damage * 3 / 2;
        // §3 Curse of the Elements: +% урона совпадающей школы по проклятой цели (амплификация магического урона).
        damage = (uint)PeriodicsService.CurseAmplify(session, creature.Guid, info.School, (int)damage);
        var (_, overkill, died) = session.World.ApplyCreatureDamage(creature, damage);

        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgSpellNonMeleeDamageLog,
            SpellPackets.BuildDamageLog(creature.Guid, (ulong)session.InWorldGuid, spellId, damage, overkill, info.School, crit), ct);
        await session.World.BroadcastCreatureHealthAsync(creature, ct);

        // M12 Spell QA: захват прямого урона (если активна сессия захвата — иначе no-op).
        await spellTestCapture.RecordDamageAsync(session, spellId, info, damage, overkill, ct);

        // PROC.1/PROC.2: прок на вредный спелл — здесь известны крит и школа (для крит-проков типа Elemental Focus).
        await procs.TryProcAsync(session, ProcFlag.DealHarmfulSpell, ct, wasCrit: crit, spellSchoolMask: info.School);

        if (died)
        {
            await killReward.OnCreatureKilledAsync(session, creature, ct); // M6.6: ролл лута + lootable-флаг
            session.Logger.LogInformation("SPELL KILL '{User}' убил '{Name}' спеллом {Spell}",
                session.Account, creature.Template.Name, spellId);
            return;
        }

        // §4 break-on-damage: урон спеллом ломает Polymorph/Disorient/Fear на цели (стан/рут/немота — нет).
        if (damage > 0)
            await crowdControl.TryBreakOnDamageAsync(session.World, creature, now, ct);

        // M7 #13: урон спеллом (в т.ч. с дистанции) вводит существо в ответный бой (как landed-удар мили).
        await creatureAi.EnsureCreatureRetaliationAsync(session, creature, roar: true, ct);
    }

    /// <summary>Монотонный id сплайна для движущих эффектов игрока (SMSG_MONSTER_MOVE). M7 #33.
    /// Static: глобальный счётчик (не состояние сервиса), инкремент атомарный.</summary>
    private static int _splineId;

    /// <summary>Базовая скорость рывка (ярд/с), как BASE_CHARGE_SPEED в CMaNGOS. M7 #33.</summary>
    private const float ChargeSpeed = 25f;

    /// <summary>
    /// Рывок (SPELL_EFFECT_CHARGE): двигает ИГРОКА к цели линейным сплайном (SMSG_MONSTER_MOVE с guid игрока —
    /// самому игроку и соседям; клиент двигает свой персонаж по сплайну). Точка приземления — у цели со
    /// смещением назад к кастеру (combat reach), Z цели. Обновляет авторитетную позицию сессии. M7 #33.
    /// </summary>
    internal async Task ApplyChargeAsync(WorldSession session, ulong targetGuid, CancellationToken ct)
    {
        if (session.Player is not { } player || targetGuid == 0)
            return;

        // Позиция цели (существо или игрок).
        float tx, ty, tz;
        if (session.World.FindCreature(targetGuid) is { } creature)
        {
            tx = creature.X; ty = creature.Y; tz = creature.Z;
        }
        else if (session.World.FindPlayer(targetGuid) is { } tp)
        {
            tx = tp.X; ty = tp.Y; tz = tp.Z;
        }
        else
        {
            return; // цель не найдена в мире
        }

        float sx = session.PosX, sy = session.PosY, sz = session.PosZ;
        float dx = tx - sx, dy = ty - sy;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist < 0.5f)
            return; // уже вплотную

        const float Stop = 1.5f; // не приземляться ВНУТРЬ цели (combat reach)
        var k = MathF.Max(0f, (dist - Stop) / dist);
        float lx = sx + dx * k, ly = sy + dy * k, lz = tz;
        var durationMs = (uint)MathF.Max(100f, dist / ChargeSpeed * 1000f);
        var splineId = (uint)System.Threading.Interlocked.Increment(ref _splineId);

        var packet = MonsterMove.Build((ulong)session.InWorldGuid, sx, sy, sz, lx, ly, lz, durationMs, splineId);
        await session.SendAsync(WorldOpcode.SmsgMonsterMove, packet, ct);                    // самому игроку
        await session.World.BroadcastToNeighborsAsync(player, WorldOpcode.SmsgMonsterMove, packet, ct); // соседям

        session.PosX = lx; session.PosY = ly; session.PosZ = lz;
        session.PosO = MathF.Atan2(dy, dx); // лицом к цели
        session.Logger.LogDebug("CHARGE '{User}' → ({X:F1};{Y:F1};{Z:F1}) dist={D:F1}", session.Account, lx, ly, lz, dist);
    }

    /// <summary>
    /// Телепорт игрока (Blink/Shadowstep, M7 #33): вперёд по направлению (Blink, ~20 ярдов) или за спину цели
    /// (Shadowstep). Мгновенно — MSG_MOVE_TELEPORT_ACK игроку (клиент применяет позицию + отвечает) +
    /// MSG_MOVE_TELEPORT наблюдателям. Обновляет авторитетную позицию сессии. Z — рельеф (вперёд) или Z цели.
    /// </summary>
    internal async Task ApplyTeleportAsync(WorldSession session, ulong targetGuid, bool behind, CancellationToken ct)
    {
        if (session.Player is not { } player)
            return;

        float nx, ny, nz, no;
        if (behind)
        {
            // За спину цели (по её ориентации). Цель — существо или игрок.
            float tx, ty, tz, to;
            if (session.World.FindCreature(targetGuid) is { } cr)
            {
                tx = cr.X; ty = cr.Y; tz = cr.Z; to = cr.O;
            }
            else if (session.World.FindPlayer(targetGuid) is { } tp)
            {
                tx = tp.X; ty = tp.Y; tz = tp.Z; to = tp.Session.PosO;
            }
            else
            {
                return;
            }
            const float Reach = 1.5f;
            nx = tx - MathF.Cos(to) * Reach;
            ny = ty - MathF.Sin(to) * Reach;
            nz = tz;
            no = to; // лицом туда же, куда цель (стоим за спиной)
        }
        else
        {
            // Вперёд по направлению взгляда (Blink ~20 ярдов); Z — по рельефу.
            const float Dist = 20f;
            nx = session.PosX + MathF.Cos(session.PosO) * Dist;
            ny = session.PosY + MathF.Sin(session.PosO) * Dist;
            no = session.PosO;
            var map = session.Character?.Map ?? 0;
            nz = terrain.GetHeight(map, nx, ny) ?? session.PosZ;
        }

        var guid = (ulong)session.InWorldGuid;
        await session.SendAsync(WorldOpcode.MsgMoveTeleportAck,
            MovementPackets.BuildTeleportAck(guid, session.NextTeleportCounter(), nx, ny, nz, no), ct);
        await session.World.BroadcastToNeighborsAsync(player, WorldOpcode.MsgMoveTeleport,
            MovementPackets.BuildTeleport(guid, nx, ny, nz, no), ct);

        session.PosX = nx; session.PosY = ny; session.PosZ = nz; session.PosO = no;
        session.Logger.LogDebug("TELEPORT '{User}' → ({X:F1};{Y:F1};{Z:F1}) behind={B}", session.Account, nx, ny, nz, behind);
    }

    /// <summary>
    /// Величина урона спелла (M10.4a): мили-абилка (WeaponDamage) — бросок урона оружия + бонус
    /// (BasePoints); процентная (WeaponPercent) — доля от урона оружия; иначе — школьный урон диапазоном.
    /// M10.6: модификаторы талантов — величина ЭФФЕКТА (ALL_EFFECTS/EFFECT{N}, напр. Improved Cleave
    /// растит бонус, не бросок оружия — как CMaNGOS CalculateSpellEffectValue), затем итог (SPELLMOD_DAMAGE).
    /// </summary>
    private static uint ComputeDamage(WorldSession session, SpellCatalog.SpellInfo info, byte comboPoints = 0)
    {
        var mods = session.Progression.SpellMods;
        int value, floor;
        if (info.WeaponPercent > 0)
        {
            var percent = SpellModifiers.ApplyEffectValue(mods, info, info.DirectEffectIndex, (int)info.WeaponPercent);
            var total = Math.Max(1, WeaponRoll(session) * percent / 100);
            value = SpellModifiers.Apply(mods, info, SpellModOp.Damage, total);
            floor = 1;
        }
        else
        {
            var bonus = info.MaxAmount > 0 ? Random.Shared.Next(info.MinAmount, info.MaxAmount + 1) : 0;
            bonus = SpellModifiers.ApplyEffectValue(mods, info, info.DirectEffectIndex, bonus);
            if (info.WeaponDamage)
            {
                value = SpellModifiers.Apply(mods, info, SpellModOp.Damage, WeaponRoll(session) + bonus);
                floor = 1;
            }
            else
            {
                value = SpellModifiers.Apply(mods, info, SpellModOp.Damage, bonus);
                floor = 0;
            }
        }
        // CP.3: финишер (Eviscerate) — бонус к урону за каждое израсходованное очко серии.
        if (comboPoints > 0 && info.ComboDamagePerPoint > 0)
            value += comboPoints * info.ComboDamagePerPoint;
        // Фаза 2: % наносимого урона по школе от активных аур (Shadowform +15% Shadow / Avenging Wrath +20% all).
        value = DamageDoneModifier.Apply(session, info.School, value);
        return (uint)Math.Max(floor, value);
    }

    /// <summary>Случайный бросок урона оружия главной руки (min..max из RefreshMeleeAsync). M10.4a.</summary>
    private static int WeaponRoll(WorldSession session)
    {
        var min = (int)MathF.Max(1f, session.Combat.WeaponMinDamage);
        var max = (int)MathF.Max(min, session.Combat.WeaponMaxDamage);
        return Random.Shared.Next(min, max + 1);
    }

    /// <summary>
    /// Применяет хил (M6.4 инкр.3): цель — игрок (себя при SELF/собственном guid, иначе указанный
    /// дружественный игрок; фолбэк — себя). Поднимает HP до максимума, шлёт SMSG_SPELLHEALLOG (зелёное
    /// число + овёрхил) и VALUES-апдейт HP наблюдателям. Реализуемо благодаря авторитетному HP из M6.7.
    /// </summary>
    internal async Task ApplyHealAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetGuid, CancellationToken ct)
    {
        var caster = (ulong)session.InWorldGuid;

        // M12 Spell QA: лечебный манекен (дружественное существо). Хилы в проекте — игрок-only, но для проверки
        // лечащих спеллов нужна стационарная цель: лечим HP существа с клампом ½ макс. (всегда ранен → effective>0).
        if (targetGuid != 0 && session.World.FindCreature(targetGuid) is { } healDummy
            && Protocol.Npcs.IsHealDummy(healDummy.Template.Entry) && healDummy.IsAlive)
        {
            var critH = RollSpellCrit(session);
            var amt = ComputeHealAmount(session, info);
            if (critH) amt = amt * 3 / 2; // CRIT.1: крит-хил ×1.5
            // §8 Wound Poison: входящее лечение цели снижено на % дебаффа лечения от кастера.
            var healCutDummy = PeriodicsService.HealReductionPctFor(session, healDummy.Guid);
            if (healCutDummy > 0)
                amt = amt * (uint)(100 - healCutDummy) / 100;
            var ceiling = healDummy.MaxHealth - healDummy.MaxHealth / 2; // не выше ½ макс. — остаётся раненым
            var beforeHp = healDummy.Health;
            healDummy.Health = Math.Min(ceiling, beforeHp + amt);
            var effHp = healDummy.Health - beforeHp;
            var overHp = amt - effHp;
            await session.World.BroadcastToObserversAsync(healDummy, WorldOpcode.SmsgSpellHealLog,
                SpellPackets.BuildHealLog(healDummy.Guid, caster, spellId, effHp, overHp, critH), ct);
            await session.World.BroadcastCreatureHealthAsync(healDummy, ct);
            await spellTestCapture.RecordHealAsync(session, spellId, info, amt, effHp, overHp, ct);
            return;
        }

        var target = targetGuid == 0 || targetGuid == caster
            ? session.Player
            : session.World.FindPlayer(targetGuid) ?? session.Player;
        if (target is null || target.Session.Combat.IsDead) // мёртвого хилом не поднять (это воскрешение)
            return;

        var ts = target.Session;
        var critHeal = RollSpellCrit(session);
        var amount = ComputeHealAmount(session, info);
        if (critHeal) amount = amount * 3 / 2; // CRIT.1: крит-хил ×1.5
        var before = ts.Combat.Health;
        ts.Combat.Health = Math.Min(ts.Combat.MaxHealth, before + amount);
        var effective = ts.Combat.Health - before;
        var overheal = amount - effective;

        await session.World.BroadcastToPlayerObserversAsync(target, WorldOpcode.SmsgSpellHealLog,
            SpellPackets.BuildHealLog(target.Guid, caster, spellId, effective, overheal, critHeal), ct);
        await session.World.BroadcastPlayerHealthAsync(target, ct);

        // M12 Spell QA: захват прямого хила (если активна сессия захвата — иначе no-op).
        await spellTestCapture.RecordHealAsync(session, spellId, info, amount, effective, overheal, ct);
    }

    /// <summary>CRIT.1: ролл спелл-крита по <see cref="Net.SessionState.SessionCastState.SpellCritChance"/> (% флэт).</summary>
    private static bool RollSpellCrit(WorldSession session)
        => session.Cast.SpellCritChance > 0 && Random.Shared.Next(100) < session.Cast.SpellCritChance;

    /// <summary>Величина хила (M6.4): бросок MinAmount..MaxAmount + модификаторы кастера (эффект + итог, M10.6).</summary>
    private static uint ComputeHealAmount(WorldSession session, SpellCatalog.SpellInfo info)
    {
        var mods = session.Progression.SpellMods;
        var rolled = Random.Shared.Next(info.MinAmount, info.MaxAmount + 1);
        rolled = SpellModifiers.ApplyEffectValue(mods, info, info.DirectEffectIndex, rolled);
        return (uint)Math.Max(0, SpellModifiers.Apply(mods, info, SpellModOp.Damage, rolled));
    }
}
