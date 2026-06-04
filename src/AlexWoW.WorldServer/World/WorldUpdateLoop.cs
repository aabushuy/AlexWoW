using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Серверный тик мира (M6.3): фиксированный цикл ~250 мс, продвигающий боевые свинги, смерть и
/// респавн существ (<see cref="WorldState.UpdateAsync"/>). Фундамент под реген/ИИ/нормализацию
/// времени движения (позже). Один на сервер (hosted service).
/// </summary>
public sealed class WorldUpdateLoop(WorldState world, ILogger<WorldUpdateLoop> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Тик мира запущен (интервал {Ms} мс)", TickInterval.TotalMilliseconds);
        using var timer = new PeriodicTimer(TickInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await world.UpdateAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Ошибка в тике мира: {Msg}", ex.Message);
            }
        }
    }
}
