using AlexWoW.Database.Abstractions;
using AlexWoW.Web.Services;
using AlexWoW.Web.Services.Kanban;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages;

/// <summary>
/// Карточка тикета канбан-доски (KB4): просмотр/создание/редактирование всех полей + лента комментариев.
/// Дерево (Project→Epic→Task/Bug) валидируется сервисом. Только админам. /Ticket — создание, /Ticket?id= — правка.
/// </summary>
public sealed class TicketModel(KanbanService kanban, ICharacterRepository characters, IAccountRepository accounts) : PageModel
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
            Input = new InputModel
            {
                Id = t.Id, Title = t.Title, Type = t.Type, Priority = t.Priority, Status = t.Status,
                ProjectId = t.ProjectId, EpicId = t.EpicId, Assignee = t.Assignee, TesterGuid = t.TesterGuid,
                ClientCheck = t.ClientCheck, Description = t.Description, TestSteps = t.TestSteps, ExpectedResult = t.ExpectedResult,
            };
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        await LoadListsAsync(ct);
        var t = new KanbanTicket
        {
            Id = Input.Id, Title = Input.Title ?? "", Type = Input.Type, Priority = Input.Priority, Status = Input.Status,
            ProjectId = Input.ProjectId, EpicId = Input.EpicId, Assignee = Input.Assignee ?? "", TesterGuid = Input.TesterGuid,
            ClientCheck = Input.ClientCheck, Description = Input.Description, TestSteps = Input.TestSteps, ExpectedResult = Input.ExpectedResult,
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
        // Исполнители: «Агент ИИ» сверху + все админ-аккаунты.
        var admins = await accounts.GetAdminUsernamesAsync(ct);
        Assignees = [AiAssignee, .. admins];
    }
}
