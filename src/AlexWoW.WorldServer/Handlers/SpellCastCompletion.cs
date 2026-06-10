using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Завершение каста (M6.4, выделено из SpellCaster в M7 S3): SPELL_GO + расход ресурса + кулдаун +
/// применение эффектов (<see cref="SpellEffectsService"/>/<see cref="PeriodicsService"/>/<see cref="CraftingService"/>)
/// + движущий эффект и цепочка триггера (M7 #33). Отложенное завершение каста с временем — точно по
/// времени каста (Task.Delay, поколение каста), см. <see cref="ScheduleDeferredCompletion"/>.
/// </summary>
internal sealed class SpellCastCompletion(SpellCatalog spellCatalog, SpellGoSender spellGo,
    ManaRegenService manaRegen, CombatResourcesService combatResources,
    SpellEffectsService spellEffects, PeriodicsService periodics, CraftingService crafting)
{
    /// <summary>
    /// Откладывает завершение каста ТОЧНО на время каста (Task.Delay, не грубый 250-мс тик): завершаем,
    /// только если каст не отменён/не перебит новым (поколение каста). Fire-and-forget осознанно —
    /// исключения логируются внутри, session живёт дольше задачи. M6.4.
    /// </summary>
    internal void ScheduleDeferredCompletion(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetGuid, byte castCount, int gen)
    {
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

    /// <summary>Завершение каста: SPELL_GO + расход маны + кулдаун + применение эффекта (урон/хил) + лог.</summary>
    internal async Task CompleteCastAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetGuid, byte castCount, CancellationToken ct)
    {
        await spellGo.SendSpellGoAsync(session, spellId, targetGuid, castCount, ct);

        // Расход ресурса: мана (правило 5 секунд: реген паузится от LastSpellCastMs) — или ярость/энергия
        // для мили-абилок (списание + апдейт полоски). M10.4a.
        var now = Environment.TickCount64;
        session.LastSpellCastMs = now;
        var cost = SpellCastService.EffectivePowerCost(session, info);
        if (cost > 0)
        {
            if (info.PowerType == SpellCastService.PowerMana)
            {
                if (session.MaxMana > 0)
                {
                    session.Mana = session.Mana > cost ? session.Mana - cost : 0;
                    await manaRegen.SendManaUpdateAsync(session, ct);
                }
            }
            else
                await combatResources.SpendPowerAsync(session, info.PowerType, cost, ct);
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
            await spellEffects.ApplyHealAsync(session, spellId, info, targetGuid, ct);
        else if (info.MaxAmount > 0 || info.WeaponDamage || info.WeaponPercent > 0)
            await spellEffects.ApplyDamageAsync(session, spellId, info, targetGuid, now, ct);

        // M10.4b: периодическая аура (DoT/HoT) — поверх прямого эффекта (напр. Immolate: удар + DoT).
        if (info.Periodic)
            await periodics.ApplyAsync(session, spellId, info, targetGuid, ct);
        // M10.4c: непериодический бафф/дебафф (Battle Shout, Curse of Weakness, Fortitude и т.п.).
        if (info.AuraBuff)
            await periodics.ApplyAuraEffectAsync(session, spellId, info, targetGuid, ct);

        // M11.3: крафт профессии — расход реагентов, создание предмета, прокачка навыка.
        if (info.CreateItemId != 0)
            await crafting.DoCraftAsync(session, spellId, info, ct);

        // M7 #33: движущий эффект — рывок к цели (Charge) или телепорт (Blink/Shadowstep).
        await ApplyMovementAsync(session, info.Movement, targetGuid, ct);
        // Цепочка триггера (Shadowstep 36554 → 36563 с эффектом телепорта): резолвим тип у триггера.
        if (info.TriggerSpellId != 0)
        {
            var trig = await spellCatalog.GetAsync(info.TriggerSpellId, ct);
            if (trig is not null)
                await ApplyMovementAsync(session, trig.Movement, targetGuid, ct);
        }
    }

    /// <summary>Применяет движущий эффект: рывок (сплайн) или телепорт вперёд/за спину цели. M7 #33.</summary>
    private Task ApplyMovementAsync(WorldSession session, SpellCatalog.SpellMovement movement,
        ulong targetGuid, CancellationToken ct) => movement switch
    {
        SpellCatalog.SpellMovement.Charge => spellEffects.ApplyChargeAsync(session, targetGuid, ct),
        SpellCatalog.SpellMovement.TeleportForward => spellEffects.ApplyTeleportAsync(session, targetGuid, behind: false, ct),
        SpellCatalog.SpellMovement.TeleportBehind => spellEffects.ApplyTeleportAsync(session, targetGuid, behind: true, ct),
        _ => Task.CompletedTask,
    };
}
