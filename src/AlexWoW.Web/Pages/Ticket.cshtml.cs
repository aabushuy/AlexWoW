using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Util;
using AlexWoW.Web.Services;
using AlexWoW.Web.Services.Kanban;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages;

/// <summary>
/// Карточка тикета канбан-доски (KB4): просмотр/создание/редактирование всех полей + лента комментариев.
/// Дерево (Project→Epic→Task/Bug) валидируется сервисом. Только админам. /Ticket — создание, /Ticket?id= — правка.
/// </summary>
public sealed partial class TicketModel(
    KanbanService kanban,
    ICharacterRepository characters,
    IAccountRepository accounts,
    SpellPreviewService spellPreview) : PageModel
{
    public const string AiAssignee = "Агент ИИ"; // #3.1: исполнитель — ИИ-агент (концептуальный id -1)

    public bool Configured => kanban.Configured;
    public bool IsNew => Input.Id == 0;
    public string? Error { get; private set; }

    [BindProperty] public InputModel Input { get; set; } = new();
    public IReadOnlyList<KanbanTicket> Projects { get; private set; } = [];
    public IReadOnlyList<KanbanTicket> Epics { get; private set; } = [];
    public IReadOnlyList<KanbanComment> Comments { get; private set; } = [];
    public IReadOnlyList<Database.Models.Character> Testers { get; private set; } = [];
    public IReadOnlyList<string> Assignees { get; private set; } = [];
    public IReadOnlyList<string> AllLabels { get; private set; } = [];
    public bool IsArchive { get; private set; }
    public DateTime? DoneAt { get; private set; }

    /// <summary>Preview-блок (Phase E) — заполняется, если в title распознан spell_id и mangos.spell_template нашёл запись.</summary>
    public SpellPreview? Spell { get; private set; }

    public sealed class InputModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Type { get; set; } = "Task";
        public string Priority { get; set; } = "Minor";
        public string Status { get; set; } = "Backlog";
        public int? ProjectId { get; set; }
        public int? EpicId { get; set; }
        public string Assignee { get; set; } = "";
        public uint? TesterGuid { get; set; }
        public bool ClientCheck { get; set; }
        public string? Description { get; set; }
        public string? TestSteps { get; set; }
        public string? ExpectedResult { get; set; }
        /// <summary>Метки: одна строка с разделителями «запятая/перевод строки» (Jira-style tag-input).</summary>
        public string? LabelsCsv { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        if (!Configured)
            return Page();
        await LoadListsAsync(ct);
        if (id is { } tid)
        {
            var (t, comments) = await kanban.GetAsync(tid, ct);
            if (t is null)
                return NotFound();
            Comments = comments;
            IsArchive = t.IsArchive;
            DoneAt = t.DoneAt;
            Input = new InputModel
            {
                Id = t.Id,
                Title = t.Title,
                Type = t.Type,
                Priority = t.Priority,
                Status = t.Status,
                ProjectId = t.ProjectId,
                EpicId = t.EpicId,
                Assignee = t.Assignee,
                TesterGuid = t.TesterGuid,
                ClientCheck = t.ClientCheck,
                Description = t.Description,
                TestSteps = t.TestSteps,
                ExpectedResult = t.ExpectedResult,
                LabelsCsv = string.Join(", ", t.Labels),
            };

            // Phase E: если в title распознан spell_id — подтянуть тултип-блок из spell_template.
            // Доступно для всех тикетов, не только regression — title типа «#11366 · …» бывают и в M10 эпиках.
            if (KanbanTitleParser.TryParseSpellId(t.Title) is int spellId)
                Spell = await spellPreview.GetAsync((uint)spellId, ct);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        await LoadListsAsync(ct);
        var labels = (Input.LabelsCsv ?? "")
            .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var t = new KanbanTicket
        {
            Id = Input.Id,
            Title = Input.Title ?? "",
            Type = Input.Type,
            Priority = Input.Priority,
            Status = Input.Status,
            ProjectId = Input.ProjectId,
            EpicId = Input.EpicId,
            Assignee = Input.Assignee ?? "",
            TesterGuid = Input.TesterGuid,
            ClientCheck = Input.ClientCheck,
            Description = Input.Description,
            TestSteps = Input.TestSteps,
            ExpectedResult = Input.ExpectedResult,
            Labels = labels,
        };
        try
        {
            if (Input.Id > 0)
            {
                await kanban.UpdateAsync(t, ct);
                return RedirectToPage(new { id = Input.Id });
            }
            var newId = await kanban.CreateAsync(t, ct);
            return RedirectToPage(new { id = newId });
        }
        catch (KanbanValidationException ex)
        {
            Error = ex.Message;
            if (Input.Id > 0)
                Comments = (await kanban.GetAsync(Input.Id, ct)).Comments;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostArchiveAsync(int id, bool archive, CancellationToken ct)
    {
        await kanban.SetArchiveAsync(id, archive, ct);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCommentAsync(int id, string? body, CancellationToken ct)
    {
        try { await kanban.CommentAsync(id, User.GameAccount(), body ?? "", ct); }
        catch (KanbanValidationException) { /* пустой комментарий — игнор */ }
        return RedirectToPage(new { id });
    }

    private async Task LoadListsAsync(CancellationToken ct)
    {
        Projects = await kanban.ProjectsAsync(ct);
        Epics = await kanban.AllEpicsAsync(ct);
        Testers = await characters.GetTestersAsync(ct);
        AllLabels = await kanban.AllLabelsAsync(ct);
        // Исполнители: «Агент ИИ» сверху + все админ-аккаунты.
        var admins = await accounts.GetAdminUsernamesAsync(ct);
        Assignees = [AiAssignee, .. admins];
    }
}
