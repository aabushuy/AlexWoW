using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Серверный тик мира (M6.3): фиксированный цикл ~250 мс, продвигающий боевые свинги, смерть и
/// респавн существ (<see cref="WorldTick.UpdateAsync"/>; тик — DI-синглтон, M7 S3). Фундамент под
/// реген/ИИ/нормализацию времени движения (позже). Один на сервер (hosted service).
/// </summary>
internal sealed class WorldUpdateLoop(WorldTick tick, SpellCatalog spellCatalog, ILogger<WorldUpdateLoop> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Расширение рангов toggle/эксклюзивных аур из БД ДО первых кастов (многоранговые ауры/аспекты/брони).
        await spellCatalog.ExpandRankTogglesAsync(stoppingToken);
        logger.LogInformation("Тик мира запущен (интервал {Ms} мс)", TickInterval.TotalMilliseconds);
        using var timer = new PeriodicTimer(TickInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await tick.UpdateAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ошибка в тике мира: {Msg}", ex.Message);
            }
        }
    }
}
