using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Оркестрация каста спелла (M6.4, DI-сервис M7 S3 — бывший SpellCaster): разбор CMSG_CAST_SPELL, гейты
/// (каст-в-процессе/кулдаун/GCD/ресурс/реагенты), каст-бар (SMSG_SPELL_START), отмена/прерывание движением.
/// Завершение каста (SPELL_GO + расход + эффекты) — <see cref="SpellCastCompletion"/>; данные спеллов —
/// <see cref="SpellCatalog"/>; переключатели — <see cref="SpellTogglesService"/> (SRP-разбиение).
/// </summary>
internal sealed class SpellCastService(SpellCatalog spellCatalog, SpellGoSender spellGo,
    SpellCastCompletion completion, SpellTogglesService spellToggles, CraftingService crafting)
{
    // --- SpellCastResult (3.3.5a, сверено с CMaNGOS SpellDefines.h) ---
    private const byte CastResultNotReady = 0x43;        // 67  — спелл на кулдауне/GCD
    private const byte CastResultNoPower = 0x55;         // 85  — не хватает маны
    private const byte CastResultSpellInProgress = 0x69; // 105 — уже идёт другой каст
    private const byte CastResultReagents = 0x64;        // 100 — не хватает реагентов (крафт) M11.3
    private const byte CastResultNoComboPoints = 0x4E;   // 78  — финишер без очков серии (SPELL_FAILED_NO_COMBO_POINTS) CP.3
    private const byte CastResultCasterAurastate = 0x16; // 22  — нельзя из-за ауры кастера (Forbearance) — CMaNGOS SPELL_FAILED_CASTER_AURASTATE
    private const byte SpellFailedInterrupted = 0x28;

    /// <summary>Толеранс GCD-гейта (мс): не режем каст у границы GCD из-за скью клиент/сервер. M10.3.</summary>
    private const long GcdToleranceMs = 250;

    /// <summary>Дистанция (ярды²) сдвига, прерывающая каст (поворот на месте не считается). M6.4.</summary>
    private const float InterruptMoveSq = 0.25f; // ~0.5 ярда

    /// <summary>Разбор CMSG_CAST_SPELL и запуск каста (точка входа из <see cref="SpellHandlers"/>).</summary>
    internal async Task HandleCastAsync(WorldSession session, IncomingPacket packet, CancellationToken ct)
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
        if (await spellToggles.TryToggleAsync(session, spellId, castCount, ct))
            return;

        // IMMUNITY.2: Forbearance — Divine Shield/Protection/Hand of Protection/Lay on Hands/Avenging Wrath
        // нельзя применить, пока висит дебафф 25771 (2 мин). Отказ → клиент пишет «Вы пока не можете…».
        if (SpellCatalog.IsForbearanceSpell(spellId)
            && session.Progression.Auras.Any(a => a.SpellId == SpellCatalog.ForbearanceDebuffId
                && (a.ExpiresAtMs == 0 || Environment.TickCount64 < a.ExpiresAtMs)))
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                SpellPackets.BuildCastFailed(castCount, spellId, CastResultCasterAurastate), ct);
            return;
        }

        // M10.3: уже идёт каст-тайм спелл → новый каст нельзя (как на оффе: кнопка блокируется, текущий
        // каст сбивается только движением/прыжком). Без этого в окне «GCD истёк, но каст ещё идёт»
        // (каст > 1.5с) клиент перезапускал каст по повторному нажатию.
        if (session.Cast.CastingSpellId != 0)
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                SpellPackets.BuildCastFailed(castCount, spellId, CastResultSpellInProgress), ct);
            return;
        }

        // M10.2: эффект спелла из spell_template (с кэшем; фолбэк — легаси-словарь при недоступности БД).
        var info = await spellCatalog.GetAsync(spellId, ct);
        if (info is null)
        {
            // Неизвестный спелл — не ломаем клиент: шлём GO без эффекта (снимает «каст»).
            session.Logger.LogDebug("CAST '{User}': spell={Spell} не найден (ни spell_template, ни легаси) → GO без эффекта",
                session.Account, spellId);
            await spellGo.SendSpellGoAsync(session, spellId, 0, castCount, ct);
            return;
        }

        // M10.6: модификаторы талантов на каст-тайм/кулдаун — копией SpellInfo (кэш не трогаем); всё
        // ниже (SPELL_START, отложенное завершение, КД в CompleteCast) видит уже изменённые значения.
        // Стоимость модифицируется внутри EffectivePowerCost.
        var mods = session.Progression.SpellMods;
        if (mods.Count > 0)
        {
            var castMs = Math.Max(0, SpellModifiers.Apply(mods, info, SpellModOp.CastingTime, info.CastMs));
            var cooldownMs = Math.Max(0, SpellModifiers.Apply(mods, info, SpellModOp.Cooldown, info.CooldownMs));
            if (castMs != info.CastMs || cooldownMs != info.CooldownMs)
                info = info with { CastMs = castMs, CooldownMs = cooldownMs };
        }

        var now = Environment.TickCount64;
        var cost = EffectivePowerCost(session, info);

        // Кулдаун: спелл ещё не готов → отказ (клиент снимет предсказанный каст, покажет ошибку).
        if (session.Cast.SpellCooldowns.TryGetValue(spellId, out var readyAt) && now < readyAt)
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                SpellPackets.BuildCastFailed(castCount, spellId, CastResultNotReady), ct);
            return;
        }

        // GCD (M10.3): глобальный кулдаун от предыдущего каста (StartRecoveryTime, обычно 1500мс). Клиент
        // его предсказывает сам, поэтому это анти-спам; толеранс — против скью клиент/сервер (не режем
        // легитимный каст у границы GCD, только явный спам).
        if (info.GcdMs > 0 && session.Cast.GcdEndMs - now > GcdToleranceMs)
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

        // §2/M11.3: нет реагентов → отказ ДО старта каст-бара (как на оффе). Для ЛЮБОГО спелла с реагентами:
        // крафт (травы), осколок души ЧК (призывы/Soulstone/Healthstone/Soul Fire — item 6265), реагенты буффов.
        if (info.Reagents is not null && !crafting.HasReagents(session, info))
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                SpellPackets.BuildCastFailed(castCount, spellId, CastResultReagents), ct);
            return;
        }

        // RUNE.3: рунная абилка DK — не хватает готовых рун нужных типов → отказ (NO_POWER → «Недостаточно
        // энергии»). Стоимость не флэтовая (резолвится по типам рун), поэтому отдельно от ресурс-гейта выше.
        if (info.PowerType == PowerRune && RuneService.GetCost(info) is { } runeCost
            && !RuneService.CanAfford(session, runeCost))
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                SpellPackets.BuildCastFailed(castCount, spellId, CastResultNoPower), ct);
            return;
        }

        // CP.3: финишер (Eviscerate/Rupture/Slice and Dice/Kidney Shot) без накопленных очков серии → отказ.
        if (info.IsFinisher && session.Combat.ComboPoints == 0)
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                SpellPackets.BuildCastFailed(castCount, spellId, CastResultNoComboPoints), ct);
            return;
        }

        // Запускаем GCD от этого каста (для последующих).
        if (info.GcdMs > 0)
            session.Cast.GcdEndMs = now + info.GcdMs;

        if (info.CastMs <= 0)
        {
            await completion.CompleteCastAsync(session, spellId, info, targetGuid, castCount, ct); // мгновенный
            return;
        }

        // Каст с временем: каст-бар у клиента (timer). Завершение — ТОЧНО по времени каста через
        // Task.Delay (не грубый 250-мс тик): иначе GO опаздывает на 0–250 мс после заполнения полоски,
        // клиент не дожидается, шлёт CANCEL_CAST → рассинхрон, анимация каста залипает.
        session.Cast.CastingSpellId = spellId;
        session.Cast.CastStartX = session.PosX; // для прерывания при сдвиге
        session.Cast.CastStartY = session.PosY;
        var gen = ++session.Cast.CastGeneration;
        await session.SendAsync(WorldOpcode.SmsgSpellStart,
            SpellPackets.BuildSpellStart((ulong)session.InWorldGuid, spellId, castCount, (uint)info.CastMs, targetGuid), ct);
        session.Logger.LogDebug("CAST start '{User}': spell={Spell} target={Target} ({Ms}мс)",
            session.Account, spellId, targetGuid, info.CastMs);

        completion.ScheduleDeferredCompletion(session, spellId, info, targetGuid, castCount, gen);
    }

    // Типы ресурса (UNIT power types): мана/ярость/энергия.
    internal const byte PowerMana = 0;
    private const byte PowerRage = 1;
    private const byte PowerEnergy = 3;
    internal const byte PowerRune = 5;  // POWER_RUNE — руны DK (стоимость не флэт, резолвится RuneService)
    private const byte PowerRunic = 6;  // POWER_RUNIC_POWER — сила рун DK (флэт ManaCost, как ярость/энергия)

    /// <summary>
    /// Стоимость ресурса спелла (M10.2 → M10.4a): для маны — флэт из spell_template или % MaxMana (приближение
    /// базовой маны); для ярости/энергии — флэт в единицах ресурса (ярость в DBC уже ×10, как у нас).
    /// M10.6: поверх базы — модификаторы SPELLMOD_COST талантов (напр. Improved Heroic Strike: −10 ярости ×10).
    /// Чистый хелпер (static): зовётся и гейтом каста, и завершением (<see cref="SpellCastCompletion"/>).
    /// </summary>
    internal static uint EffectivePowerCost(WorldSession session, SpellCatalog.SpellInfo info)
    {
        var cost = BasePowerCost(session, info);
        if (cost == 0 || session.Progression.SpellMods.Count == 0)
            return cost;
        return (uint)Math.Max(0,
            SpellModifiers.Apply(session.Progression.SpellMods, info, SpellModOp.Cost, (int)cost));
    }

    private static uint BasePowerCost(WorldSession session, SpellCatalog.SpellInfo info)
    {
        if (info.PowerType != PowerMana)
            return info.ManaCost; // ярость/энергия — флэт
        if (info.ManaCost > 0)
            return info.ManaCost;
        if (info.ManaCostPct > 0 && session.Cast.MaxMana > 0)
            return Math.Max(1u, info.ManaCostPct * session.Cast.MaxMana / 100);
        return 0;
    }

    /// <summary>Текущий запас ресурса кастера по типу (мана/ярость/энергия). M10.4a.</summary>
    private static uint CurrentPower(WorldSession session, byte powerType) => powerType switch
    {
        PowerRage => session.Combat.Rage,
        PowerEnergy => session.Combat.Energy,
        PowerRunic => session.Combat.RunicPower,
        _ => session.Cast.Mana,
    };

    /// <summary>Клиент отменил каст (Esc) — снимаем pending, эффект не применяем.</summary>
    internal void CancelCast(WorldSession session) => session.Cast.CastingSpellId = 0;

    /// <summary>
    /// Прерывает текущий каст при сдвиге игрока (вызывается из MovementHandlers). Клиент на движении
    /// гасит каст-бар локально, но НЕ шлёт CANCEL_CAST — без серверного прерывания эффект применялся
    /// бы всё равно, а анимация каста залипала. Шлём SMSG_SPELL_FAILURE (чисто гасит каст у клиента).
    /// </summary>
    internal async Task InterruptOnMoveAsync(WorldSession session, CancellationToken ct)
    {
        var spellId = session.Cast.CastingSpellId;
        if (spellId == 0)
            return;
        var dx = session.PosX - session.Cast.CastStartX;
        var dy = session.PosY - session.Cast.CastStartY;
        if (dx * dx + dy * dy < InterruptMoveSq)
            return; // только поворот/смена фейсинга — не прерываем
        session.Cast.CastingSpellId = 0;
        await session.SendAsync(WorldOpcode.SmsgSpellFailure,
            SpellPackets.BuildSpellFailure((ulong)session.InWorldGuid, spellId, SpellFailedInterrupted), ct);
        session.Logger.LogDebug("CAST interrupt (move) '{User}': spell={Spell}", session.Account, spellId);
    }
}
