using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Спеллы (M6.4): каст по цели → каст-бар (для спеллов с временем каста) → эффект (прямой урон).
/// Клиент валидирует механику по своему Spell.dbc, поэтому серверный парсер DBC пока не нужен —
/// эффект/школа/время каста хардкожены в <see cref="Spells"/>. Урон применяется общим путём с мили
/// (<see cref="World.WorldState.ApplyCreatureDamage"/>). Завершение каста — в серверном тике (M6.3 loop).
/// Хил/мана/кулдауны и парсер Spell.dbc — следующий инкремент.
/// </summary>
public static class SpellHandlers
{
    /// <summary>
    /// МАСКИ школ магии (SpellSchoolMask, u8) — в SMSG_SPELLNONMELEEDAMAGELOG поле school читается
    /// как маска, не индекс (CMaNGOS шлёт <c>uint8(schoolMask)</c>): Fire=0x4, Frost=0x10 (а НЕ 2/4).
    /// </summary>
    private const byte SchoolFire = 0x04;
    private const byte SchoolFrost = 0x10;

    /// <summary>Минимальный «справочник спеллов» (эффект): id → школа, урон, время каста (мс).</summary>
    private sealed record SpellInfo(byte School, int MinDamage, int MaxDamage, int CastMs);

    private static readonly Dictionary<uint, SpellInfo> Spells = new()
    {
        [133] = new(SchoolFire, 14, 22, 1500),   // Fireball rank 1 (req level 1)
        [116] = new(SchoolFrost, 14, 20, 1500),  // Frostbolt rank 1 (req level 1)
        [2136] = new(SchoolFire, 24, 32, 0),     // Fire Blast rank 1 (мгновенный; req level 6)
    };

    /// <summary>Спеллы, выдаваемые игроку в SMSG_INITIAL_SPELLS (для каста). M6.4 инкремент 1.</summary>
    public static readonly int[] GrantedCombatSpells = { 133, 116 };

    // --- CastFlags: START без HAS_TRAJECTORY (0x2 держал бы каст «в полёте» у снарядов и не завершал);
    //     GO = 0x100 (UNKNOWN9, как CMaNGOS). Ни один не требует conditional-полей в 3.3.5. ---
    private const uint StartFlags = 0x0;
    private const uint GoFlags = 0x100;
    private const uint TargetFlagUnit = 0x2; // SPELL_CAST_TARGET_FLAG_UNIT

    [WorldOpcodeHandler(WorldOpcode.CmsgCastSpell)]
    public static async Task OnCastSpell(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var castCount = r.UInt8();
        var spellId = r.UInt32();
        r.UInt8();                     // client cast flags — не используем
        var targetFlags = r.UInt32();  // SpellCastTargets.target_flags (3.3.5: u32)
        ulong targetGuid = 0;
        if ((targetFlags & TargetFlagUnit) != 0)
            targetGuid = r.PackedGuid();

        session.CastCount = castCount;
        session.CastTargetGuid = targetGuid;

        if (!Spells.TryGetValue(spellId, out var info))
        {
            // Неизвестный спелл — не ломаем клиент: шлём GO без эффекта (снимает «каст»).
            await SendSpellGoAsync(session, spellId, 0, ct);
            return;
        }

        if (info.CastMs <= 0)
        {
            await CompleteCastAsync(session, spellId, info, targetGuid, ct); // мгновенный
            return;
        }

        // Каст с временем: каст-бар у клиента (timer). Завершение — ТОЧНО по времени каста через
        // Task.Delay (не грубый 250-мс тик): иначе GO опаздывает на 0–250 мс после заполнения полоски,
        // клиент не дожидается, шлёт CANCEL_CAST → рассинхрон, анимация каста залипает.
        session.CastingSpellId = spellId;
        session.CastStartX = session.PosX; // для прерывания при сдвиге
        session.CastStartY = session.PosY;
        var gen = ++session.CastGeneration;
        await session.SendAsync(WorldOpcode.SmsgSpellStart,
            BuildSpellStart(session, spellId, castCount, (uint)info.CastMs, targetGuid), ct);
        session.Logger.LogDebug("CAST start '{User}': spell={Spell} target={Target} ({Ms}мс)",
            session.Account, spellId, targetGuid, info.CastMs);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(info.CastMs);
                // Каст не отменён/не перебит новым кастом за это время?
                if (session.CastGeneration == gen && session.CastingSpellId == spellId && session.InWorldGuid != 0)
                {
                    session.CastingSpellId = 0;
                    await CompleteCastAsync(session, spellId, info, targetGuid, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                session.Logger.LogDebug("Завершение каста '{User}': {Msg}", session.Account, ex.Message);
            }
        });
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgCancelCast)]
    public static Task OnCancelCast(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        // Клиент отменил каст (Esc) — снимаем pending, эффект не применяем.
        session.CastingSpellId = 0;
        return Task.CompletedTask;
    }

    /// <summary>Дистанция (ярды²) сдвига, прерывающая каст (поворот на месте не считается). M6.4.</summary>
    private const float InterruptMoveSq = 0.25f; // ~0.5 ярда

    /// <summary>
    /// Прерывает текущий каст при сдвиге игрока (вызывается из MovementHandlers). Клиент на движении
    /// гасит каст-бар локально, но НЕ шлёт CANCEL_CAST — без серверного прерывания эффект применялся
    /// бы всё равно, а анимация каста залипала. Шлём SMSG_SPELL_FAILURE (чисто гасит каст у клиента).
    /// </summary>
    internal static async Task InterruptOnMoveAsync(WorldSession session, CancellationToken ct)
    {
        var spellId = session.CastingSpellId;
        if (spellId == 0)
            return;
        var dx = session.PosX - session.CastStartX;
        var dy = session.PosY - session.CastStartY;
        if (dx * dx + dy * dy < InterruptMoveSq)
            return; // только поворот/смена фейсинга — не прерываем
        session.CastingSpellId = 0;
        await session.SendAsync(WorldOpcode.SmsgSpellFailure, BuildSpellFailure(session, spellId, 0x28), ct); // INTERRUPTED
        session.Logger.LogDebug("CAST interrupt (move) '{User}': spell={Spell}", session.Account, spellId);
    }

    /// <summary>SMSG_SPELL_FAILURE (3.3.5): u64 caster + u8 extra_casts + u32 spell + u8 result.</summary>
    private static byte[] BuildSpellFailure(WorldSession session, uint spellId, byte result)
        => new ByteWriter(16)
            .UInt64((ulong)session.InWorldGuid)
            .UInt8(0)
            .UInt32(spellId)
            .UInt8(result)
            .ToArray();

    /// <summary>Завершение каста: SPELL_GO + применение эффекта (урон цели) + лог.</summary>
    private static async Task CompleteCastAsync(WorldSession session, uint spellId, SpellInfo info,
        ulong targetGuid, CancellationToken ct)
    {
        await SendSpellGoAsync(session, spellId, targetGuid, ct);

        var creature = targetGuid != 0 ? session.World.FindCreature(targetGuid) : null;
        if (creature is null || !creature.IsAlive)
            return; // цель пропала/мертва — спелл «впустую»

        var damage = (uint)Random.Shared.Next(info.MinDamage, info.MaxDamage + 1);
        var (_, overkill, died) = session.World.ApplyCreatureDamage(creature, damage);

        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgSpellNonMeleeDamageLog,
            BuildDamageLog(creature.Guid, (ulong)session.InWorldGuid, spellId, damage, overkill, info.School), ct);
        await session.World.BroadcastCreatureHealthAsync(creature, ct);

        if (died)
            session.Logger.LogInformation("SPELL KILL '{User}' убил '{Name}' спеллом {Spell}",
                session.Account, creature.Template.Name, spellId);
    }

    /// <summary>SMSG_SPELL_START (3.3.5): каст-бар. flags=0x2, без conditional-полей.</summary>
    private static byte[] BuildSpellStart(WorldSession session, uint spellId, byte castCount, uint timerMs, ulong targetGuid)
    {
        var caster = (ulong)session.InWorldGuid;
        var w = new ByteWriter(48);
        PackedGuid.Write(w, caster);   // cast_item = caster (без предмета-кастера)
        PackedGuid.Write(w, caster);   // caster
        w.UInt8(castCount);
        w.UInt32(spellId);
        w.UInt32(StartFlags);
        w.UInt32(timerMs);
        WriteTargets(w, targetGuid);
        return w.ToArray();
    }

    /// <summary>SMSG_SPELL_GO (3.3.5): спелл «пошёл». flags=0x100, без conditional-полей.</summary>
    private static async Task SendSpellGoAsync(WorldSession session, uint spellId, ulong targetGuid, CancellationToken ct)
    {
        var caster = (ulong)session.InWorldGuid;
        var w = new ByteWriter(48);
        PackedGuid.Write(w, caster);   // cast_item
        PackedGuid.Write(w, caster);   // caster
        w.UInt8(0);                    // extra_casts
        w.UInt32(spellId);
        w.UInt32(GoFlags);
        w.UInt32((uint)Environment.TickCount); // timestamp
        if (targetGuid != 0)
        {
            w.UInt8(1);                // amount_of_hits
            w.UInt64(targetGuid);      // hits[0] (plain Guid)
        }
        else
        {
            w.UInt8(0);                // нет хитов
        }
        w.UInt8(0);                    // amount_of_misses
        WriteTargets(w, targetGuid);
        var body = w.ToArray();
        await session.SendAsync(WorldOpcode.SmsgSpellGo, body, ct);   // кастеру (снаряд)
        if (session.Player is { } player)                            // и наблюдателям
            await session.World.BroadcastToNeighborsAsync(player, WorldOpcode.SmsgSpellGo, body, ct);
    }

    /// <summary>SpellCastTargets: только SELF (нет цели) или UNIT (packed guid цели).</summary>
    private static void WriteTargets(ByteWriter w, ulong targetGuid)
    {
        if (targetGuid != 0)
        {
            w.UInt32(TargetFlagUnit);
            PackedGuid.Write(w, targetGuid);
        }
        else
        {
            w.UInt32(0); // SELF
        }
    }

    /// <summary>SMSG_SPELLNONMELEEDAMAGELOG (3.3.5): «числа урона» от спелла.</summary>
    private static byte[] BuildDamageLog(ulong target, ulong attacker, uint spellId, uint damage, uint overkill, byte school)
    {
        var w = new ByteWriter(48);
        PackedGuid.Write(w, target);
        PackedGuid.Write(w, attacker);
        w.UInt32(spellId);
        w.UInt32(damage);
        w.UInt32(overkill);
        w.UInt8(school);
        w.UInt32(0)   // absorbed
         .UInt32(0)   // resisted
         .UInt8(0)    // periodic_log
         .UInt8(0)    // unused
         .UInt32(0)   // blocked
         .UInt32(0)   // hit_info
         .UInt8(0);   // extend_flag
        return w.ToArray();
    }
}
