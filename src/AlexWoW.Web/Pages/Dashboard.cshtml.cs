using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages;

/// <summary>
/// Дашборд прогресса (одностраничный): срез 1 — БД <c>project</c> (механики/абилки/таланты/расы),
/// срез 2 — трекер Vikunja (доменные проекты P01..P40). Read-only; доступен без логина (прогресс-инфо).
/// </summary>
[AllowAnonymous]
public sealed class DashboardModel(ProjectDashboardService project, VikunjaDashboardService tracker) : PageModel
{
    public ProjectDashboardService.DashboardData? Db { get; private set; }
    public IReadOnlyList<VikunjaDashboardService.TrackerProject>? Tracker { get; private set; }
    public bool DbConfigured => project.Configured;
    public bool TrackerConfigured => tracker.Configured;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Db = await project.GetAsync(ct);
        Tracker = await tracker.GetAsync(ct);
    }
}
