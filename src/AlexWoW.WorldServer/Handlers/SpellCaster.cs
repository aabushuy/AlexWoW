using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Оркестрация каста спелла (M6.4): разбор CMSG_CAST_SPELL, гейты (кулдаун/мана), каст-бар (SMSG_SPELL_START)
/// → завершение ТОЧНО по времени каста (Task.Delay, поколение каста) → SMSG_SPELL_GO + расход маны + кулдаун +
/// эффект (<see cref="SpellEffects"/>). Прерывание движением. Данные спеллов — <see cref="SpellCatalog"/>,
/// сборка пакетов — <see cref="SpellPackets"/>, переключатели — <see cref="SpellToggles"/> (SRP-разбиение).
/// </summary>
public static class SpellCaster
{
    // --- SpellCastResult (3.3.5a, сверено с CMaNGOS SpellDefines.h) ---
    private const byte CastResultNotReady = 0x43;        // 67  — спелл на кулдауне/GCD
    private const byte CastResultNoPower = 0x55;         // 85  — не хватает маны
    private const byte CastResultSpellInProgress = 0x69; // 105 — уже идёт другой каст
    private const byte SpellFailedInterrupted = 0x28;

    /// <summary>Толеранс GCD-гейта (мс): не режем каст у границы GCD из-за скью клиент/сервер. M10.3.</summary>
    private const long GcdToleranceMs = 250;

    /// <summary>Дистанция (ярды²) сдвига, прерывающая каст (поворот на месте не считается). M6.4.</summary>
    private const float InterruptMoveSq = 0.25f; // ~0.5 ярда

    /// <summary>Разбор CMSG_CAST_SPELL и запуск каста (точка входа из <see cref="SpellHandlers"/>).</summary>
    internal static async Task HandleCastAsync(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var castCount = r.UInt8();
        var spellId = r.UInt32();
        r.UInt8();                     // client cast flags — не используем
        var targetFlags = r.UInt32();  // SpellCastTargets.target_flags (3.3.5: u32)
        ulong targetGuid = 0;
        if ((targetFlags & SpellPackets.TargetFlagUnit) != 0)
            targetGuid = r.PackedGuid();

        // M6.12/M7 #21: переключатели (стойки/ауры/аспекты) — мгновенная перманентная аура, без маны/цели/КД.
        if (await SpellToggles.TryToggleAsync(session, spellId, castCount, ct))
            return;

        // M10.3: уже идёт каст-тайм спелл → новый каст нельзя (как на оффе: кнопка блокируется, текущий
        // каст сбивается только движением/прыжком). Без этого в окне «GCD истёк, но каст ещё идёт»
        // (каст > 1.5с) клиент перезапускал каст по повторному нажатию.
        if (session.CastingSpellId != 0)
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                SpellPackets.BuildCastFailed(castCount, spellId, CastResultSpellInProgress), ct);
            return;
        }

        // M10.2: эффект спелла из spell_template (с кэшем; фолбэк — легаси-словарь при недоступности БД).
        var info = await SpellCatalog.GetAsync(session.WorldDb, spellId, session.Logger, ct);
        if (info is null)
        {
            // Неизвестный спелл — не ломаем клиент: шлём GO без эффекта (снимает «каст»).
            session.Logger.LogDebug("CAST '{User}': spell={Spell} не найден (ни spell_template, ни легаси) → GO без эффекта",
                session.Account, spellId);
            await SendSpellGoAsync(session, spellId, 0, castCount, ct);
            return;
        }

        var now = Environment.TickCount64;
        var cost = EffectivePowerCost(session, info);

        // Кулдаун: спелл ещё не готов → отказ (клиент снимет предсказанный каст, покажет ошибку).
        if (session.SpellCooldowns.TryGetValue(spellId, out var readyAt) && now < readyAt)
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                SpellPackets.BuildCastFailed(castCount, spellId, CastResultNotReady), ct);
            return;
        }

        // GCD (M10.3): глобальный кулдаун от предыдущего каста (StartRecoveryTime, обычно 1500мс). Клиент
        // его предсказывает сам, поэтому это анти-спам; толеранс — против скью клиент/сервер (не режем
        // легитимный каст у границы GCD, только явный спам).
        if (info.GcdMs > 0 && session.GcdEndMs - now > GcdToleranceMs)
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                SpellPackets.BuildCastFailed(castCount, spellId, CastResultNotReady), ct);
            return;
        }

        // Ресурс (мана/ярость/энергия): не хватает → отказ (NO_POWER → «Недостаточно …» у клиента). Списываем
        // при завершении (CompleteCast), а проверяем на старте — между ними ресурс только регенится. M10.4a.
        if (cost > 0 && CurrentPower(session, info.PowerType) < cost)
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                SpellPackets.BuildCastFailed(castCount, spellId, CastResultNoPower), ct);
            return;
        }

        // Запускаем GCD от этого каста (для последующих).
        if (info.GcdMs > 0)
            session.GcdEndMs = now + info.GcdMs;

        if (info.CastMs <= 0)
        {
            await CompleteCastAsync(session, spellId, info, targetGuid, castCount, ct); // мгновенный
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
            SpellPackets.BuildSpellStart((ulong)session.InWorldGuid, spellId, castCount, (uint)info.CastMs, targetGuid), ct);
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
                    await CompleteCastAsync(session, spellId, info, targetGuid, castCount, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                session.Logger.LogDebug("Завершение каста '{User}': {Msg}", session.Account, ex.Message);
            }
        });
    }

    // Типы ресурса (UNIT power types): мана/ярость/энергия.
    private const byte PowerMana = 0;
    private const byte PowerRage = 1;
    private const byte PowerEnergy = 3;

    /// <summary>
    /// Стоимость ресурса спелла (M10.2 → M10.4a): для маны — флэт из spell_template или % MaxMana (приближение
    /// базовой маны); для ярости/энергии — флэт в единицах ресурса (ярость в DBC уже ×10, как у нас).
    /// </summary>
    private static uint EffectivePowerCost(WorldSession session, SpellCatalog.SpellInfo info)
    {
        if (info.PowerType != PowerMana)
            return info.ManaCost; // ярость/энергия — флэт
        if (info.ManaCost > 0)
            return info.ManaCost;
        if (info.ManaCostPct > 0 && session.MaxMana > 0)
            return Math.Max(1u, info.ManaCostPct * session.MaxMana / 100);
        return 0;
    }

    /// <summary>Текущий запас ресурса кастера по типу (мана/ярость/энергия). M10.4a.</summary>
    private static uint CurrentPower(WorldSession session, byte powerType) => powerType switch
    {
        PowerRage => session.Rage,
        PowerEnergy => session.Energy,
        _ => session.Mana,
    };

    /// <summary>Клиент отменил каст (Esc) — снимаем pending, эффект не применяем.</summary>
    internal static void CancelCast(WorldSession session) => session.CastingSpellId = 0;

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
        await session.SendAsync(WorldOpcode.SmsgSpellFailure,
            SpellPackets.BuildSpellFailure((ulong)session.InWorldGuid, spellId, SpellFailedInterrupted), ct);
        session.Logger.LogDebug("CAST interrupt (move) '{User}': spell={Spell}", session.Account, spellId);
    }

    /// <summary>Завершение каста: SPELL_GO + расход маны + кулдаун + применение эффекта (урон/хил) + лог.</summary>
    private static async Task CompleteCastAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetGuid, byte castCount, CancellationToken ct)
    {
        await SendSpellGoAsync(session, spellId, targetGuid, castCount, ct);

        // Расход ресурса: мана (правило 5 секунд: реген паузится от LastSpellCastMs) — или ярость/энергия
        // для мили-абилок (списание + апдейт полоски). M10.4a.
        var now = Environment.TickCount64;
        session.LastSpellCastMs = now;
        var cost = EffectivePowerCost(session, info);
        if (cost > 0)
        {
            if (info.PowerType == PowerMana)
            {
                if (session.MaxMana > 0)
                {
                    session.Mana = session.Mana > cost ? session.Mana - cost : 0;
                    await ManaRegen.SendManaUpdateAsync(session, ct);
                }
            }
            else
                await CombatResources.SpendPowerAsync(session, info.PowerType, cost, ct);
        }

        // Кулдаун: запускаем у клиента (полоска на кнопке) и запоминаем для отказа при раннем рекасте.
        if (info.CooldownMs > 0)
        {
            session.SpellCooldowns[spellId] = now + info.CooldownMs;
            await session.SendAsync(WorldOpcode.SmsgSpellCooldown,
                SpellPackets.BuildSpellCooldown((ulong)session.InWorldGuid, spellId, (uint)info.CooldownMs), ct);
        }

        // Прямой эффект: хил, либо урон (если есть прямой урон — чистый DoT без прямого числа не шлём).
        if (info.IsHeal)
            await SpellEffects.ApplyHealAsync(session, spellId, info, targetGuid, ct);
        else if (info.MaxAmount > 0 || info.WeaponDamage || info.WeaponPercent > 0)
            await SpellEffects.ApplyDamageAsync(session, spellId, info, targetGuid, now, ct);

        // M10.4b: периодическая аура (DoT/HoT) — поверх прямого эффекта (напр. Immolate: удар + DoT).
        if (info.Periodic)
            await Periodics.ApplyAsync(session, spellId, info, targetGuid, ct);
        // M10.4c: непериодический бафф/дебафф (Battle Shout, Curse of Weakness, Fortitude и т.п.).
        if (info.AuraBuff)
            await Periodics.ApplyAuraEffectAsync(session, spellId, info, targetGuid, ct);

        // M7 #33: движущий эффект — рывок к цели (Charge/Intercept/Intervene/Feral Charge).
        if (info.Movement == SpellCatalog.SpellMovement.Charge)
            await SpellEffects.ApplyChargeAsync(session, targetGuid, ct);
    }

    /// <summary>
    /// SMSG_SPELL_GO кастеру (снаряд) и наблюдателям (broadcast). Общая точка для обычного каста и
    /// переключателей (<see cref="SpellToggles"/>).
    /// </summary>
    internal static async Task SendSpellGoAsync(WorldSession session, uint spellId, ulong targetGuid, byte castCount, CancellationToken ct)
    {
        // cast_count берём ПАРАМЕТРОМ, а не из session.CastCount: повторное нажатие во время каста
        // перезатирает session.CastCount, и GO завершения исходного каста ушёл бы с чужим счётчиком →
        // клиент не сопоставит GO со своим pending-кастом → залипание завершения каста (M10.4a фикс #26).
        var body = SpellPackets.BuildSpellGo((ulong)session.InWorldGuid, spellId, targetGuid, castCount);
        await session.SendAsync(WorldOpcode.SmsgSpellGo, body, ct);   // кастеру (снаряд)
        if (session.Player is { } player)                            // и наблюдателям
            await session.World.BroadcastToNeighborsAsync(player, WorldOpcode.SmsgSpellGo, body, ct);
    }
}
