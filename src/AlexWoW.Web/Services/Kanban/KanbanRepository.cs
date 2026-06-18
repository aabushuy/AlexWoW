using System.Data;
using AlexWoW.Web;
using Dapper;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace AlexWoW.Web.Services.Kanban;

/// <summary>
/// Доступ к канбан-доске в БД <c>project</c> (KB2). Dapper read/write, строка подключения —
/// <c>Web:ProjectConnectionString</c>. Только данные; правила дерева/перечислений — в <see cref="KanbanService"/>.
/// Метки и архив (KB12/KB13) живут в той же БД: <c>kanban_label</c>, <c>kanban_ticket_label</c>;
/// колонки <c>is_archive</c>/<c>done_at</c> добавляются ALTER'ом в <c>deploy/sql/kanban-schema.sql</c>.
/// </summary>
public sealed class KanbanRepository(IOptions<WebOptions> options)
{
    private readonly string _cs = options.Value.ProjectConnectionString;

    public bool Configured => !string.IsNullOrWhiteSpace(_cs);

    // Алиасы под имена свойств KanbanTicket (Dapper мапит по имени).
    private const string Cols =
        "id Id, title Title, description Description, test_steps TestSteps, expected_result ExpectedResult, " +
        "priority Priority, type Type, status Status, epic_id EpicId, project_id ProjectId, assignee Assignee, " +
        "tester_guid TesterGuid, client_check ClientCheck, is_archive IsArchive, done_at DoneAt, " +
        "created_at CreatedAt, updated_at UpdatedAt";

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
        if (!f.IncludeArchived) w.Add("is_archive = 0");
        if (f.Labels is { Count: > 0 } labels)
        {
            // AND-фильтр: пересечение по всем меткам. Сравнение имён нормализуем LOWER, чтобы регистр не влиял.
            w.Add("id IN (SELECT tl.ticket_id FROM project.kanban_ticket_label tl " +
                  "JOIN project.kanban_label kl ON kl.id = tl.label_id " +
                  "WHERE LOWER(kl.name) IN @lbl GROUP BY tl.ticket_id HAVING COUNT(DISTINCT kl.id) = @lblCnt)");
            p.Add("lbl", labels.Select(static x => x.Trim().ToLowerInvariant()).ToArray());
            p.Add("lblCnt", labels.Count);
        }
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
        // done_at ставится здесь же, если тикет сразу создан в Done (редкий случай, но валидный).
        return await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "INSERT INTO project.kanban_ticket " +
            "(title, description, test_steps, expected_result, priority, type, status, epic_id, project_id, assignee, tester_guid, client_check, done_at) " +
            "VALUES (@Title, @Description, @TestSteps, @ExpectedResult, @Priority, @Type, @Status, @EpicId, @ProjectId, @Assignee, @TesterGuid, @ClientCheck, " +
            "       CASE WHEN @Status='Done' THEN CURRENT_TIMESTAMP ELSE NULL END); " +
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
        // Если статус меняется в Done — выставляем done_at; если уходит из Done — сбрасываем done_at и is_archive
        // (расхороним). is_archive из UpdateAsync не трогаем напрямую — для ручного toggle см. SetArchiveAsync.
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE project.kanban_ticket SET title=@Title, description=@Description, test_steps=@TestSteps, " +
            "expected_result=@ExpectedResult, priority=@Priority, type=@Type, status=@Status, epic_id=@EpicId, " +
            "project_id=@ProjectId, assignee=@Assignee, tester_guid=@TesterGuid, client_check=@ClientCheck, " +
            "done_at = CASE WHEN @Status='Done' AND (status<>'Done' OR done_at IS NULL) THEN CURRENT_TIMESTAMP " +
            "              WHEN @Status<>'Done' THEN NULL " +
            "              ELSE done_at END, " +
            "is_archive = CASE WHEN @Status<>'Done' THEN 0 ELSE is_archive END " +
            "WHERE id=@Id",
            new
            {
                t.Id, t.Title, t.Description, t.TestSteps, t.ExpectedResult, t.Priority, t.Type, t.Status,
                t.EpicId, t.ProjectId, t.Assignee, t.TesterGuid, ClientCheck = t.ClientCheck ? 1 : 0,
            }, cancellationToken: ct));
    }

    public async Task SetStatusAsync(int id, string status, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        // Аналогично UpdateAsync — синхронизируем done_at/is_archive со статусом.
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE project.kanban_ticket SET status=@status, " +
            "done_at = CASE WHEN @status='Done' AND (status<>'Done' OR done_at IS NULL) THEN CURRENT_TIMESTAMP " +
            "              WHEN @status<>'Done' THEN NULL " +
            "              ELSE done_at END, " +
            "is_archive = CASE WHEN @status<>'Done' THEN 0 ELSE is_archive END " +
            "WHERE id=@id", new { id, status }, cancellationToken: ct));
    }

    public async Task SetTesterAsync(int id, uint? testerGuid, bool clientCheck, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE project.kanban_ticket SET tester_guid=@g, client_check=@cc WHERE id=@id",
            new { id, g = testerGuid, cc = clientCheck ? 1 : 0 }, cancellationToken: ct));
    }

    /// <summary>Ручной toggle архива.</summary>
    public async Task SetArchiveAsync(int id, bool archive, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE project.kanban_ticket SET is_archive=@a WHERE id=@id",
            new { id, a = archive ? 1 : 0 }, cancellationToken: ct));
    }

    /// <summary>Авто-архивация (KanbanArchiveBackgroundService): закрытое больше двух суток → в архив.</summary>
    public async Task<int> ArchiveStaleDoneAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        return await c.ExecuteAsync(new CommandDefinition(
            "UPDATE project.kanban_ticket SET is_archive=1 " +
            "WHERE status='Done' AND is_archive=0 AND done_at IS NOT NULL AND done_at < (NOW() - INTERVAL 2 DAY)",
            cancellationToken: ct));
    }

    public async Task<int> AddCommentAsync(int ticketId, string author, string body, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "INSERT INTO project.kanban_comment (ticket_id, author, body) VALUES (@t, @a, @b); SELECT LAST_INSERT_ID();",
            new { t = ticketId, a = author, b = body }, cancellationToken: ct));
    }

    // ───────────────── Метки (KB13) ─────────────────

    /// <summary>Все метки словаря (для автокомплита).</summary>
    public async Task<IReadOnlyList<string>> AllLabelsAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var rows = await c.QueryAsync<string>(new CommandDefinition(
            "SELECT name FROM project.kanban_label ORDER BY name", cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>Метки одного тикета.</summary>
    public async Task<IReadOnlyList<string>> LabelsForAsync(int ticketId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var rows = await c.QueryAsync<string>(new CommandDefinition(
            "SELECT l.name FROM project.kanban_ticket_label tl " +
            "JOIN project.kanban_label l ON l.id = tl.label_id " +
            "WHERE tl.ticket_id = @t ORDER BY l.name", new { t = ticketId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>Метки пачки тикетов одним запросом (для Board/Dashboard).</summary>
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> LabelsByTicketAsync(
        IReadOnlyCollection<int> ticketIds, CancellationToken ct)
    {
        if (ticketIds.Count == 0) return new Dictionary<int, IReadOnlyList<string>>();
        await using var c = await OpenAsync(ct);
        var rows = await c.QueryAsync<(int TicketId, string Name)>(new CommandDefinition(
            "SELECT tl.ticket_id TicketId, l.name Name FROM project.kanban_ticket_label tl " +
            "JOIN project.kanban_label l ON l.id = tl.label_id " +
            "WHERE tl.ticket_id IN @ids ORDER BY l.name",
            new { ids = ticketIds }, cancellationToken: ct));
        var map = new Dictionary<int, List<string>>();
        foreach (var r in rows)
        {
            if (!map.TryGetValue(r.TicketId, out var list)) map[r.TicketId] = list = new();
            list.Add(r.Name);
        }
        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
    }

    /// <summary>
    /// Переписывает метки тикета. Имена нормализуются (Trim + LOWER), пустые отбрасываются. Новые имена попадают
    /// в <c>kanban_label</c> через INSERT IGNORE. Всё под транзакцией: либо все метки заменились, либо ни одна.
    /// </summary>
    public async Task SetLabelsAsync(int ticketId, IReadOnlyCollection<string> names, CancellationToken ct)
    {
        var norm = names
            .Where(static n => !string.IsNullOrWhiteSpace(n))
            .Select(static n => n.Trim().ToLowerInvariant())
            .Where(static n => n.Length > 0 && n.Length <= 64)
            .Distinct()
            .ToArray();

        await using var c = await OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        await c.ExecuteAsync(new CommandDefinition(
            "DELETE FROM project.kanban_ticket_label WHERE ticket_id = @t",
            new { t = ticketId }, transaction: tx, cancellationToken: ct));

        if (norm.Length > 0)
        {
            // Создаём недостающие метки. INSERT IGNORE опирается на UNIQUE (name) — гонок не боимся.
            await c.ExecuteAsync(new CommandDefinition(
                "INSERT IGNORE INTO project.kanban_label (name) VALUES (@name)",
                norm.Select(n => new { name = n }), transaction: tx, cancellationToken: ct));

            await c.ExecuteAsync(new CommandDefinition(
                "INSERT INTO project.kanban_ticket_label (ticket_id, label_id) " +
                "SELECT @t, id FROM project.kanban_label WHERE name IN @names",
                new { t = ticketId, names = norm }, transaction: tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }
}
