using AlexWoW.WorldServer.Handlers;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Оркестрация серверного тика мира (SRP-часть рефактора #30, DI-синглтон M7 S3): раз в ~250 мс
/// (из <see cref="WorldUpdateLoop"/>) продвигает по-игроку (мили-свинги/реген/ауры/авто-агро/time-sync) и
/// по-существу (ИИ возврат/бой/реген, респавн мёртвых). Исключения по сессии/существу ловятся поштучно,
/// чтобы не валить весь тик. Тик-сервисы (бой/реген/ауры/периодика/time-sync) — DI (бой — S4,
/// time-sync — <see cref="Handlers.TimeSyncService"/>, S7).
/// </summary>
internal sealed class WorldTick(WorldState world, FactionStore factions,
    ManaRegenService manaRegen, CombatResourcesService combatResources, RuneService runes,
    AuraService auras, PeriodicsService periodics,
    PlayerMeleeService playerMelee, CreatureCombatAI creatureAi, RegenService regen,
    Handlers.CrowdControlService crowdControl, TimeSyncService timeSync, ILogger<WorldTick> logger)
{
    /// <summary>Период рассылки SMSG_TIME_SYNC_REQ каждому игроку (нормализация часов). M6.3 ч.2.</summary>
    private const long TimeSyncIntervalMs = 10_000;

    public async Task UpdateAsync(CancellationToken ct)
    {
        var now = Environment.TickCount64;
        await factions.EnsureLoadedAsync(ct); // M6.7: ленивая загрузка реакций фракций (один раз)

        foreach (var player in world.Players)
        {
            try
            {
                await playerMelee.TickMeleeAsync(player.Session, now, ct);
                // M6.4: завершение каста — точно по времени (Task.Delay в SpellCastCompletion), не в тике;
                // здесь — реген маны (вне «правила 5 секунд»).
                await manaRegen.TickAsync(player.Session, now, ct);
                await combatResources.TickAsync(player.Session, now, ct);            // M6.12: реген энергии / распад ярости
                await runes.TickAsync(player.Session, now, ct);                      // RUNE.2: реген рун DK по кулдауну
                await auras.TickAsync(player.Session, now, ct);                      // M6.11: истечение аур
                await periodics.TickAsync(player.Session, now, ct);                  // M10.4b: тик DoT/HoT
                await regen.TickPlayerRegenAsync(player.Session, now, ct);            // M6.7: внебоевой реген HP
                await creatureAi.TickAggroScanAsync(world, player, now, ct);          // M6.7: авто-агро по фракции

                // M6.3 ч.2: периодическая синхронизация часов клиента (для нормализации движения).
                if (now - player.Session.LastTimeSyncDispatchMs >= TimeSyncIntervalMs)
                    await timeSync.SendTimeSyncReqAsync(player.Session, ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Тик '{User}': {Msg}", player.Character.Name, ex.Message);
            }
        }

        foreach (var creature in world.Creatures)
        {
            try
            {
                // M6.7: живое существо — ИИ (возврат/преследование+бой/реген); мёртвое — ждёт респавна.
                if (creature.IsAlive)
                {
                    // Фаза 2 CC: снять истёкший контроль у ЛЮБОГО существа (в т.ч. вне боя — манекен).
                    await crowdControl.ExpireIfDueAsync(world, creature, now, ct);
                    // §8 снара (Crippling Poison): снять истёкшее замедление.
                    await crowdControl.ExpireSnareIfDueAsync(world, creature, now, ct);
                    if (creature.Evading)
                        await creatureAi.TickEvadeAsync(world, creature, now, ct);
                    else if (creature.CombatTargetGuid != 0)
                        await creatureAi.TickCreatureCombatAsync(world, creature, now, ct);
                    else
                        await regen.TickRegenAsync(world, creature, now, ct);
                    continue;
                }
                if (creature.RespawnAtMs is { } at && now >= at)
                    await world.Director.RespawnCreatureAsync(creature, ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Тик существа {Guid}: {Msg}", creature.Guid, ex.Message);
            }
        }
    }
}
