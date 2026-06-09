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
    private const byte SchoolHoly = 0x02; // для хила (в heal-логе школа не передаётся — для полноты)

    /// <summary>
    /// Минимальный «справочник спеллов» (эффект): id → школа, величина эффекта (урон ИЛИ хил), время
    /// каста (мс), стоимость маны, кулдаун (мс; 0 — только GCD у клиента), хил-ли. Rank-1 как в WotLK. M6.4.
    /// </summary>
    private sealed record SpellInfo(byte School, int MinAmount, int MaxAmount, int CastMs, uint ManaCost,
        int CooldownMs, bool IsHeal = false);

    private static readonly Dictionary<uint, SpellInfo> Spells = new()
    {
        [133] = new(SchoolFire, 14, 22, 1500, ManaCost: 30, CooldownMs: 0),     // Fireball rank 1
        [116] = new(SchoolFrost, 14, 20, 1500, ManaCost: 25, CooldownMs: 0),    // Frostbolt rank 1
        [2136] = new(SchoolFire, 24, 32, 0, ManaCost: 40, CooldownMs: 8000),    // Fire Blast rank 1 (мгновенный, КД 8с)
        [2050] = new(SchoolHoly, 45, 56, 1500, ManaCost: 30, CooldownMs: 0, IsHeal: true), // Lesser Heal rank 1
    };

    /// <summary>Спеллы, выдаваемые игроку в SMSG_INITIAL_SPELLS (для каста). M6.4.</summary>
    public static readonly int[] GrantedCombatSpells = { 133, 116, 2136, 2050 };

    // Группы эксклюзивных переключателей (M7 #21): один активен в группе.
    private const byte GroupShapeshift = 1;   // стойки воина / формы друида
    private const byte GroupPaladinAura = 2;  // ауры паладина
    private const byte GroupHunterAspect = 3; // аспекты охотника

    /// <summary>Переключатель: форма шейпшифта (0 — без формы) + группа эксклюзивности. M7 #21.</summary>
    private readonly record struct Toggle(byte Form, byte Group);

    /// <summary>
    /// Спеллы-переключатели (M6.12/M7 #21): мгновенный каст без маны/цели → перманентная аура (персист
    /// через релог). Форма (стойки воина → панель стоек). Эксклюзивны в своей группе. ⚠️ Только РАНГ 1 —
    /// высшие ранги имеют другие spell-id (нужен Spell.dbc; полноценно — в расширении системы аур).
    /// </summary>
    private static readonly Dictionary<uint, Toggle> ToggleSpells = new()
    {
        // Стойки воина (форма → панель стоек): Battle=17, Defensive=18, Berserker=19.
        [2457] = new(17, GroupShapeshift),
        [71] = new(18, GroupShapeshift),
        [2458] = new(19, GroupShapeshift),
        // Ауры паладина (эксклюзивны).
        [465] = new(0, GroupPaladinAura),    // Devotion Aura
        [7294] = new(0, GroupPaladinAura),   // Retribution Aura
        [19746] = new(0, GroupPaladinAura),  // Concentration Aura
        [32223] = new(0, GroupPaladinAura),  // Crusader Aura
        [19876] = new(0, GroupPaladinAura),  // Shadow Resistance Aura
        [19888] = new(0, GroupPaladinAura),  // Frost Resistance Aura
        [19891] = new(0, GroupPaladinAura),  // Fire Resistance Aura
        // Аспекты охотника (эксклюзивны).
        [13165] = new(0, GroupHunterAspect), // Aspect of the Hawk
        [5118] = new(0, GroupHunterAspect),  // Aspect of the Cheetah
        [13163] = new(0, GroupHunterAspect), // Aspect of the Monkey
        [13159] = new(0, GroupHunterAspect), // Aspect of the Pack
        [20043] = new(0, GroupHunterAspect), // Aspect of the Wild
        [13161] = new(0, GroupHunterAspect), // Aspect of the Beast
        [34074] = new(0, GroupHunterAspect), // Aspect of the Viper
        [61846] = new(0, GroupHunterAspect), // Aspect of the Dragonhawk
    };

    // --- SpellCastResult (3.3.5a, сверено с reference world/common.wowm) ---
    private const byte CastResultNotReady = 0x43; // спелл на кулдауне
    private const byte CastResultNoPower = 0x55;  // не хватает маны

    /// <summary>Реген маны вне «правила 5 секунд»: прибавка за тик регена. M6.4.</summary>
    private const uint ManaRegenPerSec = 20;
    /// <summary>Кадэнс регена маны (мс) — реже тика мира, чтобы не спамить апдейтами. M6.4.</summary>
    private const long ManaRegenIntervalMs = 1000;
    /// <summary>«Правило 5 секунд»: после каста реген маны паузится. M6.4.</summary>
    private const long FiveSecondRuleMs = 5000;

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

        // M6.12/M7 #21: переключатели (стойки/ауры/аспекты) — мгновенная перманентная аура (персист),
        // эксклюзивная в группе; без маны/цели/кулдауна.
        if (ToggleSpells.TryGetValue(spellId, out var toggle))
        {
            await SendSpellGoAsync(session, spellId, 0, ct); // завершить каст у клиента
            await Auras.ApplyAsync(session, spellId, durationMs: 0, positive: true, toggle.Form, ct,
                group: toggle.Group, persist: true);
            session.Logger.LogDebug("TOGGLE '{User}': spell={Spell} форма={Form} группа={Group}",
                session.Account, spellId, toggle.Form, toggle.Group);
            return;
        }

        if (!Spells.TryGetValue(spellId, out var info))
        {
            // Неизвестный спелл — не ломаем клиент: шлём GO без эффекта (снимает «каст»).
            await SendSpellGoAsync(session, spellId, 0, ct);
            return;
        }

        // Кулдаун: спелл ещё не готов → отказ (клиент снимет предсказанный каст, покажет ошибку).
        if (session.SpellCooldowns.TryGetValue(spellId, out var readyAt) && Environment.TickCount64 < readyAt)
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                BuildCastFailed(castCount, spellId, CastResultNotReady), ct);
            return;
        }

        // Мана: не хватает на каст → отказ (NO_POWER → «Недостаточно маны» у клиента). Списываем при
        // завершении (CompleteCast), а проверяем на старте — между ними мана только регенится.
        if (session.MaxMana > 0 && session.Mana < info.ManaCost)
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                BuildCastFailed(castCount, spellId, CastResultNoPower), ct);
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

    /// <summary>
    /// SMSG_CAST_FAILED (3.3.5): отказ каста. u8 cast_count + u32 spell + u8 result + u8 multiple_casts.
    /// Для NOT_READY/NO_POWER conditional-полей нет.
    /// </summary>
    private static byte[] BuildCastFailed(byte castCount, uint spellId, byte result)
        => new ByteWriter(8)
            .UInt8(castCount)
            .UInt32(spellId)
            .UInt8(result)
            .UInt8(0) // multiple_casts = false
            .ToArray();

    /// <summary>
    /// SMSG_SPELL_COOLDOWN (3.3.5): u64 guid + u8 flags + [{u32 spell, u32 cooldown_ms}] (массив до конца).
    /// Запускает кулдаун-полоску на кнопке у клиента.
    /// </summary>
    private static byte[] BuildSpellCooldown(ulong caster, uint spellId, uint cooldownMs)
        => new ByteWriter(17)
            .UInt64(caster)
            .UInt8(0)          // flags
            .UInt32(spellId)
            .UInt32(cooldownMs)
            .ToArray();

    /// <summary>
    /// Реген маны в серверном тике (M6.4): вне «правила 5 секунд» прибавляет ManaRegenPerSec раз в
    /// ManaRegenIntervalMs, апдейтит полоску. Зовётся из <see cref="World.WorldState.UpdateAsync"/>.
    /// </summary>
    internal static async Task TickManaRegenAsync(WorldSession session, long now, CancellationToken ct)
    {
        if (session.MaxMana == 0 || session.Mana >= session.MaxMana || session.InWorldGuid == 0)
            return;
        if (now - session.LastSpellCastMs < FiveSecondRuleMs)        // правило 5 секунд — реген на паузе
            return;
        if (now - session.LastManaRegenMs < ManaRegenIntervalMs)
            return;

        session.LastManaRegenMs = now;
        session.Mana = Math.Min(session.MaxMana, session.Mana + ManaRegenPerSec);
        await SendManaUpdateAsync(session, ct);
    }

    /// <summary>
    /// Шлёт текущую ману себе двумя путями: VALUES-апдейт <c>UNIT_FIELD_POWER1</c> (консистентность поля)
    /// + <c>SMSG_POWER_UPDATE</c> (0x480) — именно он надёжно двигает полоску ресурса у клиента 3.3.5a
    /// (как TrinityCore на каждом изменении power). Одного VALUES-апдейта собственному юниту не хватает. M6.4.
    /// </summary>
    private static async Task SendManaUpdateAsync(WorldSession session, CancellationToken ct)
    {
        var guid = (ulong)session.InWorldGuid;
        await session.SendAsync(WorldOpcode.SmsgUpdateObject, PlayerSpawn.BuildPowerUpdate(guid, session.Mana), ct);
        await session.SendAsync(WorldOpcode.SmsgPowerUpdate, BuildPowerUpdatePacket(guid, session.Mana), ct);
    }

    /// <summary>SMSG_POWER_UPDATE (3.3.5): PackedGuid unit + u8 power(MANA=0) + u32 amount.</summary>
    private static byte[] BuildPowerUpdatePacket(ulong guid, uint amount)
    {
        var w = new ByteWriter(16);
        PackedGuid.Write(w, guid);
        w.UInt8(0);          // Power: MANA
        w.UInt32(amount);
        return w.ToArray();
    }

    /// <summary>Завершение каста: SPELL_GO + расход маны + кулдаун + применение эффекта (урон цели) + лог.</summary>
    private static async Task CompleteCastAsync(WorldSession session, uint spellId, SpellInfo info,
        ulong targetGuid, CancellationToken ct)
    {
        await SendSpellGoAsync(session, spellId, targetGuid, ct);

        // Расход маны (правило 5 секунд: реген паузится от LastSpellCastMs) + апдейт полоски себе.
        var now = Environment.TickCount64;
        session.LastSpellCastMs = now;
        if (session.MaxMana > 0 && info.ManaCost > 0)
        {
            session.Mana = session.Mana > info.ManaCost ? session.Mana - info.ManaCost : 0;
            await SendManaUpdateAsync(session, ct);
        }

        // Кулдаун: запускаем у клиента (полоска на кнопке) и запоминаем для отказа при раннем рекасте.
        if (info.CooldownMs > 0)
        {
            session.SpellCooldowns[spellId] = now + info.CooldownMs;
            await session.SendAsync(WorldOpcode.SmsgSpellCooldown,
                BuildSpellCooldown((ulong)session.InWorldGuid, spellId, (uint)info.CooldownMs), ct);
        }

        if (info.IsHeal)
        {
            await ApplyHealAsync(session, spellId, info, targetGuid, ct);
            return;
        }

        var creature = targetGuid != 0 ? session.World.FindCreature(targetGuid) : null;
        if (creature is null || !creature.IsAlive)
            return; // цель пропала/мертва — спелл «впустую»

        session.LastCombatMs = now; // M6.7: урон спеллом — пауза внебоевого регена HP
        var damage = (uint)Random.Shared.Next(info.MinAmount, info.MaxAmount + 1);
        var (_, overkill, died) = session.World.ApplyCreatureDamage(creature, damage);

        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgSpellNonMeleeDamageLog,
            BuildDamageLog(creature.Guid, (ulong)session.InWorldGuid, spellId, damage, overkill, info.School), ct);
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

    /// <summary>
    /// Применяет хил (M6.4 инкр.3): цель — игрок (себя при SELF/собственном guid, иначе указанный
    /// дружественный игрок; фолбэк — себя). Поднимает HP до максимума, шлёт SMSG_SPELLHEALLOG (зелёное
    /// число + овёрхил) и VALUES-апдейт HP наблюдателям. Реализуемо благодаря авторитетному HP из M6.7.
    /// </summary>
    private static async Task ApplyHealAsync(WorldSession session, uint spellId, SpellInfo info,
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
            BuildHealLog(target.Guid, caster, spellId, effective, overheal), ct);
        await session.World.BroadcastPlayerHealthAsync(target, ct);
    }

    /// <summary>SMSG_SPELLHEALLOG (3.3.5): packed victim + packed caster + spell + amount + overheal +
    /// absorb(0) + crit(u8) + unused(u8). Величина — эффективный хил (овёрхил отдельным полем).</summary>
    private static byte[] BuildHealLog(ulong victim, ulong caster, uint spellId, uint amount, uint overheal)
    {
        var w = new ByteWriter(32);
        PackedGuid.Write(w, victim);
        PackedGuid.Write(w, caster);
        w.UInt32(spellId)
         .UInt32(amount)
         .UInt32(overheal)
         .UInt32(0)   // absorb
         .UInt8(0)    // critical
         .UInt8(0);   // unused
        return w.ToArray();
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
