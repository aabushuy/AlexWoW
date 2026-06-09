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
    // --- SpellCastResult (3.3.5a, сверено с reference world/common.wowm) ---
    private const byte CastResultNotReady = 0x43; // спелл на кулдауне
    private const byte CastResultNoPower = 0x55;  // не хватает маны
    private const byte SpellFailedInterrupted = 0x28;

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

        session.CastCount = castCount;
        session.CastTargetGuid = targetGuid;

        // M6.12/M7 #21: переключатели (стойки/ауры/аспекты) — мгновенная перманентная аура, без маны/цели/КД.
        if (await SpellToggles.TryToggleAsync(session, spellId, ct))
            return;

        // M10.2: эффект спелла из spell_template (с кэшем; фолбэк — легаси-словарь при недоступности БД).
        var info = await SpellCatalog.GetAsync(session.WorldDb, spellId, session.Logger, ct);
        if (info is null)
        {
            // Неизвестный спелл — не ломаем клиент: шлём GO без эффекта (снимает «каст»).
            session.Logger.LogDebug("CAST '{User}': spell={Spell} не найден (ни spell_template, ни легаси) → GO без эффекта",
                session.Account, spellId);
            await SendSpellGoAsync(session, spellId, 0, ct);
            return;
        }

        var manaCost = EffectiveManaCost(session, info);

        // Кулдаун: спелл ещё не готов → отказ (клиент снимет предсказанный каст, покажет ошибку).
        if (session.SpellCooldowns.TryGetValue(spellId, out var readyAt) && Environment.TickCount64 < readyAt)
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                SpellPackets.BuildCastFailed(castCount, spellId, CastResultNotReady), ct);
            return;
        }

        // Мана: не хватает на каст → отказ (NO_POWER → «Недостаточно маны» у клиента). Списываем при
        // завершении (CompleteCast), а проверяем на старте — между ними мана только регенится.
        if (session.MaxMana > 0 && session.Mana < manaCost)
        {
            await session.SendAsync(WorldOpcode.SmsgCastFailed,
                SpellPackets.BuildCastFailed(castCount, spellId, CastResultNoPower), ct);
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
                    await CompleteCastAsync(session, spellId, info, targetGuid, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                session.Logger.LogDebug("Завершение каста '{User}': {Msg}", session.Account, ex.Message);
            }
        });
    }

    /// <summary>
    /// Стоимость маны спелла (M10.2): флэтом из spell_template, иначе процентом базовой маны кастера.
    /// База — текущий MaxMana (приближение CreateMana; бонус от интеллекта при экипировке слегка завышает
    /// стоимость на высоких уровнях — уточним в M10.3/M10.4). Не-мана-классам стоимость не считаем.
    /// </summary>
    private static uint EffectiveManaCost(WorldSession session, SpellCatalog.SpellInfo info)
    {
        if (info.ManaCost > 0)
            return info.ManaCost;
        if (info.ManaCostPct > 0 && session.MaxMana > 0)
            return Math.Max(1u, info.ManaCostPct * session.MaxMana / 100);
        return 0;
    }

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
        ulong targetGuid, CancellationToken ct)
    {
        await SendSpellGoAsync(session, spellId, targetGuid, ct);

        // Расход маны (правило 5 секунд: реген паузится от LastSpellCastMs) + апдейт полоски себе.
        var now = Environment.TickCount64;
        session.LastSpellCastMs = now;
        var manaCost = EffectiveManaCost(session, info);
        if (session.MaxMana > 0 && manaCost > 0)
        {
            session.Mana = session.Mana > manaCost ? session.Mana - manaCost : 0;
            await ManaRegen.SendManaUpdateAsync(session, ct);
        }

        // Кулдаун: запускаем у клиента (полоска на кнопке) и запоминаем для отказа при раннем рекасте.
        if (info.CooldownMs > 0)
        {
            session.SpellCooldowns[spellId] = now + info.CooldownMs;
            await session.SendAsync(WorldOpcode.SmsgSpellCooldown,
                SpellPackets.BuildSpellCooldown((ulong)session.InWorldGuid, spellId, (uint)info.CooldownMs), ct);
        }

        if (info.IsHeal)
            await SpellEffects.ApplyHealAsync(session, spellId, info, targetGuid, ct);
        else
            await SpellEffects.ApplyDamageAsync(session, spellId, info, targetGuid, now, ct);
    }

    /// <summary>
    /// SMSG_SPELL_GO кастеру (снаряд) и наблюдателям (broadcast). Общая точка для обычного каста и
    /// переключателей (<see cref="SpellToggles"/>).
    /// </summary>
    internal static async Task SendSpellGoAsync(WorldSession session, uint spellId, ulong targetGuid, CancellationToken ct)
    {
        var body = SpellPackets.BuildSpellGo((ulong)session.InWorldGuid, spellId, targetGuid);
        await session.SendAsync(WorldOpcode.SmsgSpellGo, body, ct);   // кастеру (снаряд)
        if (session.Player is { } player)                            // и наблюдателям
            await session.World.BroadcastToNeighborsAsync(player, WorldOpcode.SmsgSpellGo, body, ct);
    }
}
