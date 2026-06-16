using AlexWoW.Web.Services.Kanban;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages;

/// <summary>
/// Канбан-доска QA (KB3): 5 колонок статусов, карточки тикетов, фильтр по проекту/эпику, перемещение
/// между колонками. Только админам (политика "Admin", см. Program.cs). Данные — <see cref="KanbanService"/>.
/// </summary>
public sealed class BoardModel(KanbanService kanban) : PageModel
{
    public bool Configured => kanban.Configured;

    [BindProperty(SupportsGet = true)] public int? Project { get; set; }
    [BindProperty(SupportsGet = true)] public int? Epic { get; set; }

    public IReadOnlyList<KanbanTicket> Projects { get; private set; } = [];
    public IReadOnlyList<KanbanTicket> Epics { get; private set; } = [];
    public IReadOnlyList<string> Columns => KanbanVocab.Statuses;
    public Dictionary<string, List<KanbanTicket>> ByStatus { get; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        foreach (var s in KanbanVocab.Statuses)
            ByStatus[s] = [];
        if (!Configured)
            return;

        Projects = await kanban.ProjectsAsync(ct);
        if (Project is { } p)
            Epics = await kanban.EpicsAsync(p, ct);

        var tickets = await kanban.ListAsync(new KanbanFilter { ProjectId = Project, EpicId = Epic }, ct);
        foreach (var t in tickets)
            if (ByStatus.TryGetValue(t.Status, out var col))
                col.Add(t);
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, string status, int? project, int? epic, CancellationToken ct)
    {
        try { await kanban.MoveAsync(id, status, ct); }
        catch (KanbanValidationException) { /* недопустимый статус — игнор, просто вернёмся на доску */ }
        return RedirectToPage(new { Project = project, Epic = epic });
    }
}
