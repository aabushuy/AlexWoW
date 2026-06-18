using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlexWoW.Web.Services.Kanban;

/// <summary>
/// Авто-архивация закрытых тикетов: раз в час дёргает <see cref="KanbanRepository.ArchiveStaleDoneAsync"/>,
/// который помечает <c>is_archive=1</c> для тикетов в статусе Done, чей <c>done_at</c> старше 2 суток (KB12).
/// </summary>
internal sealed class KanbanArchiveBackgroundService(KanbanRepository repo, ILogger<KanbanArchiveBackgroundService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!repo.Configured)
        {
            logger.LogInformation("Канбан-архивация выключена: ProjectConnectionString пуст");
            return;
        }

        logger.LogInformation("Канбан-архивация запущена (интервал {Min} мин, порог — 2 суток в Done)", Interval.TotalMinutes);
        using var timer = new PeriodicTimer(Interval);
        // Первый проход — сразу при старте; потом по таймеру.
        do
        {
            try
            {
                var n = await repo.ArchiveStaleDoneAsync(stoppingToken);
                if (n > 0)
                    logger.LogInformation("Канбан: автоматически заархивировано тикетов: {Count}", n);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Канбан-архивация: ошибка прохода — {Msg}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
