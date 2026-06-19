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

    /// <summary>Чекбокс «Показывать архивные» (KB12). По умолчанию false — архив скрыт.</summary>
    [BindProperty(SupportsGet = true)] public bool Archived { get; set; }

    /// <summary>Выбранные метки (KB13). Может прийти как ?labels=foo&labels=bar или ?labels=foo,bar.</summary>
    [BindProperty(SupportsGet = true, Name = "labels")] public string[] LabelsRaw { get; set; } = [];

    public IReadOnlyList<string> SelectedLabels { get; private set; } = [];

    public IReadOnlyList<KanbanTicket> Projects { get; private set; } = [];
    public IReadOnlyList<KanbanTicket> Epics { get; private set; } = [];
    public IReadOnlyList<string> AllLabels { get; private set; } = [];
    public IReadOnlyList<string> Columns => KanbanVocab.Statuses;
    public Dictionary<string, List<KanbanTicket>> ByStatus { get; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        foreach (var s in KanbanVocab.Statuses)
            ByStatus[s] = [];
        if (!Configured)
            return;

        SelectedLabels = (LabelsRaw ?? [])
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .SelectMany(static x => x.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(static x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Projects = await kanban.ProjectsAsync(ct);
        if (Project is { } p)
            Epics = await kanban.EpicsAsync(p, ct);
        AllLabels = await kanban.AllLabelsAsync(ct);

        var tickets = await kanban.ListAsync(new KanbanFilter
        {
            ProjectId = Project,
            EpicId = Epic,
            IncludeArchived = Archived,
            Labels = SelectedLabels.Count > 0 ? SelectedLabels : null,
        }, ct);
        foreach (var t in tickets)
            if (ByStatus.TryGetValue(t.Status, out var col))
                col.Add(t);
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, string status, int? project, int? epic,
        bool archived, string[]? labels, CancellationToken ct)
    {
        try { await kanban.MoveAsync(id, status, ct); }
        catch (KanbanValidationException) { /* недопустимый статус — игнор, просто вернёмся на доску */ }
        return RedirectToPage(new { Project = project, Epic = epic, Archived = archived, labels });
    }

    /// <summary>Билдер query-string для ссылок-чипов меток (toggle: добавить/убрать одну метку).</summary>
    public string LinkWithLabelToggle(string label)
    {
        var set = new HashSet<string>(SelectedLabels, StringComparer.OrdinalIgnoreCase);
        if (!set.Add(label)) set.Remove(label);
        return BuildLink(set);
    }

    public string LinkWithoutLabel(string label)
    {
        var set = new HashSet<string>(SelectedLabels, StringComparer.OrdinalIgnoreCase);
        set.Remove(label);
        return BuildLink(set);
    }

    private string BuildLink(IEnumerable<string> labels)
    {
        var parts = new List<string>();
        if (Project is { } p) parts.Add($"Project={p}");
        if (Epic is { } e) parts.Add($"Epic={e}");
        if (Archived) parts.Add("Archived=true");
        foreach (var l in labels) parts.Add($"labels={Uri.EscapeDataString(l)}");
        return parts.Count == 0 ? "/Board" : "/Board?" + string.Join('&', parts);
    }
}
