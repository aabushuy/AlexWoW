using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Оркестрация серверного тика мира (SRP-часть <see cref="WorldState"/>, рефактор #30): раз в ~250 мс
/// (из <see cref="WorldUpdateLoop"/>) продвигает по-игроку (мили-свинги/реген/ауры/авто-агро/time-sync) и
/// по-существу (ИИ возврат/бой/реген, респавн мёртвых). Исключения по сессии/существу ловятся поштучно,
/// чтобы не валить весь тик.
/// </summary>
public sealed class WorldTick(WorldState world, CreatureDirector creatures, FactionStore factions, ILogger logger)
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
                await Handlers.CombatHandlers.TickMeleeAsync(player.Session, now, ct);
                // M6.4: завершение каста — точно по времени (Task.Delay в SpellHandlers), не в тике;
                // здесь — реген маны (вне «правила 5 секунд»).
                await Handlers.SpellHandlers.TickManaRegenAsync(player.Session, now, ct);
                await Handlers.CombatResources.TickAsync(player.Session, now, ct);            // M6.12: реген энергии / распад ярости
                await Handlers.Auras.TickAsync(player.Session, now, ct);                      // M6.11: истечение аур
                await Handlers.CombatHandlers.TickPlayerRegenAsync(player.Session, now, ct); // M6.7: внебоевой реген HP
                await Handlers.CombatHandlers.TickAggroScanAsync(world, player, now, ct);     // M6.7: авто-агро по фракции

                // M6.3 ч.2: периодическая синхронизация часов клиента (для нормализации движения).
                if (now - player.Session.LastTimeSyncDispatchMs >= TimeSyncIntervalMs)
                    await Handlers.WorldEntryHandlers.SendTimeSyncReqAsync(player.Session, ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug("Тик '{User}': {Msg}", player.Character.Name, ex.Message);
            }
        }

        foreach (var creature in world.Creatures)
        {
            try
            {
                // M6.7: живое существо — ИИ (возврат/преследование+бой/реген); мёртвое — ждёт респавна.
                if (creature.IsAlive)
                {
                    if (creature.Evading)
                        await Handlers.CombatHandlers.TickEvadeAsync(world, creature, now, ct);
                    else if (creature.CombatTargetGuid != 0)
                        await Handlers.CombatHandlers.TickCreatureCombatAsync(world, creature, now, ct);
                    else
                        await Handlers.CombatHandlers.TickRegenAsync(world, creature, now, ct);
                    continue;
                }
                if (creature.RespawnAtMs is { } at && now >= at)
                    await creatures.RespawnCreatureAsync(creature, ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug("Тик существа {Guid}: {Msg}", creature.Guid, ex.Message);
            }
        }
    }
}
