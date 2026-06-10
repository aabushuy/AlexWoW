using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Применение эффекта завершённого каста (M6.4): прямой урон по существу или хил игрока. Отделено от
/// оркестрации каста (<see cref="SpellCaster"/>) по SRP. Урон идёт общим путём с мили
/// (<see cref="World.WorldState.ApplyCreatureDamage"/>); хил опирается на авторитетное HP (M6.7).
/// </summary>
public static class SpellEffects
{
    /// <summary>Прямой урон спеллом по существу-цели: лог урона + HP наблюдателям + смерть/лут + ответный бой.</summary>
    internal static async Task ApplyDamageAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetGuid, long now, CancellationToken ct)
    {
        var creature = targetGuid != 0 ? session.World.FindCreature(targetGuid) : null;
        if (creature is null || !creature.IsAlive)
            return; // цель пропала/мертва — спелл «впустую»

        session.LastCombatMs = now; // M6.7: урон спеллом — пауза внебоевого регена HP
        var damage = ComputeDamage(session, info);
        var (_, overkill, died) = session.World.ApplyCreatureDamage(creature, damage);

        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgSpellNonMeleeDamageLog,
            SpellPackets.BuildDamageLog(creature.Guid, (ulong)session.InWorldGuid, spellId, damage, overkill, info.School), ct);
        await session.World.BroadcastCreatureHealthAsync(creature, ct);

        if (died)
        {
            await LootHandlers.OnCreatureKilledAsync(session, creature, ct); // M6.6: ролл лута + lootable-флаг
            session.Logger.LogInformation("SPELL KILL '{User}' убил '{Name}' спеллом {Spell}",
                session.Account, creature.Template.Name, spellId);
            return;
        }

        // M7 #13: урон спеллом (в т.ч. с дистанции) вводит существо в ответный бой (как landed-удар мили).
        await CombatHandlers.EnsureCreatureRetaliationAsync(session, creature, roar: true, ct);
    }

    /// <summary>Монотонный id сплайна для движущих эффектов игрока (SMSG_MONSTER_MOVE). M7 #33.</summary>
    private static int _splineId;

    /// <summary>Базовая скорость рывка (ярд/с), как BASE_CHARGE_SPEED в CMaNGOS. M7 #33.</summary>
    private const float ChargeSpeed = 25f;

    /// <summary>
    /// Рывок (SPELL_EFFECT_CHARGE): двигает ИГРОКА к цели линейным сплайном (SMSG_MONSTER_MOVE с guid игрока —
    /// самому игроку и соседям; клиент двигает свой персонаж по сплайну). Точка приземления — у цели со
    /// смещением назад к кастеру (combat reach), Z цели. Обновляет авторитетную позицию сессии. M7 #33.
    /// </summary>
    internal static async Task ApplyChargeAsync(WorldSession session, ulong targetGuid, CancellationToken ct)
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

        const float stop = 1.5f; // не приземляться ВНУТРЬ цели (combat reach)
        var k = MathF.Max(0f, (dist - stop) / dist);
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
    /// Величина урона спелла (M10.4a): мили-абилка (WeaponDamage) — бросок урона оружия + бонус
    /// (BasePoints); процентная (WeaponPercent) — доля от урона оружия; иначе — школьный урон диапазоном.
    /// </summary>
    private static uint ComputeDamage(WorldSession session, SpellCatalog.SpellInfo info)
    {
        if (info.WeaponPercent > 0)
            return (uint)Math.Max(1, WeaponRoll(session) * (int)info.WeaponPercent / 100);
        var bonus = info.MaxAmount > 0 ? Random.Shared.Next(info.MinAmount, info.MaxAmount + 1) : 0;
        if (info.WeaponDamage)
            return (uint)Math.Max(1, WeaponRoll(session) + bonus);
        return (uint)Math.Max(0, bonus);
    }

    /// <summary>Случайный бросок урона оружия главной руки (min..max из RefreshMeleeAsync). M10.4a.</summary>
    private static int WeaponRoll(WorldSession session)
    {
        var min = (int)MathF.Max(1f, session.WeaponMinDamage);
        var max = (int)MathF.Max(min, session.WeaponMaxDamage);
        return Random.Shared.Next(min, max + 1);
    }

    /// <summary>
    /// Применяет хил (M6.4 инкр.3): цель — игрок (себя при SELF/собственном guid, иначе указанный
    /// дружественный игрок; фолбэк — себя). Поднимает HP до максимума, шлёт SMSG_SPELLHEALLOG (зелёное
    /// число + овёрхил) и VALUES-апдейт HP наблюдателям. Реализуемо благодаря авторитетному HP из M6.7.
    /// </summary>
    internal static async Task ApplyHealAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetGuid, CancellationToken ct)
    {
        var caster = (ulong)session.InWorldGuid;
        var target = targetGuid == 0 || targetGuid == caster
            ? session.Player
            : session.World.FindPlayer(targetGuid) ?? session.Player;
        if (target is null || target.Session.IsDead) // мёртвого хилом не поднять (это воскрешение)
            return;

        var ts = target.Session;
        var amount = (uint)Random.Shared.Next(info.MinAmount, info.MaxAmount + 1);
        var before = ts.Health;
        ts.Health = Math.Min(ts.MaxHealth, before + amount);
        var effective = ts.Health - before;
        var overheal = amount - effective;

        await session.World.BroadcastToPlayerObserversAsync(target, WorldOpcode.SmsgSpellHealLog,
            SpellPackets.BuildHealLog(target.Guid, caster, spellId, effective, overheal), ct);
        await session.World.BroadcastPlayerHealthAsync(target, ct);
    }
}
