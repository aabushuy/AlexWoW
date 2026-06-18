using AlexWoW.Web.Services.Kanban;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages;

/// <summary>
/// Дашборд канбана (admin-only): сводные карточки по тикетам, прогресс-бары проектов/эпиков, нагрузка тестеров.
/// Источник истины — таблица <c>project.kanban_ticket</c> (архивные тикеты игнорируются).
/// </summary>
public sealed class DashboardModel(KanbanDashboardService dashboard) : PageModel
{
    public bool Configured => dashboard.Configured;
    public KanbanDashboardService.DashboardData Data { get; private set; } =
        new(new KanbanDashboardService.Aggregate(0, 0, 0, 0, 0, 0, 0), [], []);

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (!Configured) return;
        Data = await dashboard.LoadAsync(ct);
    }
}
