using AlexWoW.Web;
using Dapper;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace AlexWoW.Web.Services.Kanban;

/// <summary>
/// Доступ к канбан-доске в БД <c>project</c> (KB2). Dapper read/write, строка подключения —
/// <c>Web:ProjectConnectionString</c> (как у <see cref="ProjectDashboardService"/>). Только данные; правила
/// дерева/перечислений — в <see cref="KanbanService"/>.
/// </summary>
public sealed class KanbanRepository(IOptions<WebOptions> options)
{
    private readonly string _cs = options.Value.ProjectConnectionString;

    public bool Configured => !string.IsNullOrWhiteSpace(_cs);

    // Алиасы под имена свойств KanbanTicket (Dapper мапит по имени).
    private const string Cols =
        "id Id, title Title, description Description, test_steps TestSteps, expected_result ExpectedResult, " +
        "priority Priority, type Type, status Status, epic_id EpicId, project_id ProjectId, assignee Assignee, " +
        "tester_guid TesterGuid, client_check ClientCheck, created_at CreatedAt, updated_at UpdatedAt";

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new MySqlConnection(_cs);
        await c.OpenAsync(ct);
        return c;
    }

    public async Task<IReadOnlyList<KanbanTicket>> ListAsync(KanbanFilter f, CancellationToken ct)
    {
        var w = new List<string>();
        var p = new DynamicParameters();
        if (f.ProjectId is { } pr) { w.Add("project_id = @pr"); p.Add("pr", pr); }
        if (f.EpicId is { } ep) { w.Add("epic_id = @ep"); p.Add("ep", ep); }
        if (!string.IsNullOrEmpty(f.Status)) { w.Add("status = @st"); p.Add("st", f.Status); }
        if (!string.IsNullOrEmpty(f.Type)) { w.Add("type = @ty"); p.Add("ty", f.Type); }
        if (f.TesterGuid is { } tg) { w.Add("tester_guid = @tg"); p.Add("tg", tg); }
        if (f.ClientCheck is { } cc) { w.Add("client_check = @cc"); p.Add("cc", cc ? 1 : 0); }
        var where = w.Count == 0 ? "1=1" : string.Join(" AND ", w);

        await using var c = await OpenAsync(ct);
        var rows = await c.QueryAsync<KanbanTicket>(new CommandDefinition(
            $"SELECT {Cols} FROM project.kanban_ticket WHERE {where} ORDER BY updated_at DESC", p, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<KanbanTicket?> GetAsync(int id, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<KanbanTicket>(new CommandDefinition(
            $"SELECT {Cols} FROM project.kanban_ticket WHERE id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<KanbanComment>> CommentsAsync(int ticketId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var rows = await c.QueryAsync<KanbanComment>(new CommandDefinition(
            "SELECT id Id, ticket_id TicketId, author Author, body Body, created_at CreatedAt " +
            "FROM project.kanban_comment WHERE ticket_id = @t ORDER BY created_at ASC", new { t = ticketId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<int> CreateAsync(KanbanTicket t, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "INSERT INTO project.kanban_ticket " +
            "(title, description, test_steps, expected_result, priority, type, status, epic_id, project_id, assignee, tester_guid, client_check) " +
            "VALUES (@Title, @Description, @TestSteps, @ExpectedResult, @Priority, @Type, @Status, @EpicId, @ProjectId, @Assignee, @TesterGuid, @ClientCheck); " +
            "SELECT LAST_INSERT_ID();",
            new
            {
                t.Title, t.Description, t.TestSteps, t.ExpectedResult, t.Priority, t.Type, t.Status,
                t.EpicId, t.ProjectId, t.Assignee, t.TesterGuid, ClientCheck = t.ClientCheck ? 1 : 0,
            }, cancellationToken: ct));
    }

    public async Task UpdateAsync(KanbanTicket t, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE project.kanban_ticket SET title=@Title, description=@Description, test_steps=@TestSteps, " +
            "expected_result=@ExpectedResult, priority=@Priority, type=@Type, status=@Status, epic_id=@EpicId, " +
            "project_id=@ProjectId, assignee=@Assignee, tester_guid=@TesterGuid, client_check=@ClientCheck WHERE id=@Id",
            new
            {
                t.Id, t.Title, t.Description, t.TestSteps, t.ExpectedResult, t.Priority, t.Type, t.Status,
                t.EpicId, t.ProjectId, t.Assignee, t.TesterGuid, ClientCheck = t.ClientCheck ? 1 : 0,
            }, cancellationToken: ct));
    }

    public async Task SetStatusAsync(int id, string status, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE project.kanban_ticket SET status=@status WHERE id=@id", new { id, status }, cancellationToken: ct));
    }

    public async Task SetTesterAsync(int id, uint? testerGuid, bool clientCheck, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE project.kanban_ticket SET tester_guid=@g, client_check=@cc WHERE id=@id",
            new { id, g = testerGuid, cc = clientCheck ? 1 : 0 }, cancellationToken: ct));
    }

    public async Task<int> AddCommentAsync(int ticketId, string author, string body, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "INSERT INTO project.kanban_comment (ticket_id, author, body) VALUES (@t, @a, @b); SELECT LAST_INSERT_ID();",
            new { t = ticketId, a = author, b = body }, cancellationToken: ct));
    }
}
