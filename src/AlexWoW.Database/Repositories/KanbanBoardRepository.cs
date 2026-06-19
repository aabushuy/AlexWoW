using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.Database.Util;
using Dapper;
using MySqlConnector;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// Dapper-доступ world-сервера к канбан-доске в БД <c>project</c> (KB7). Короткоживущее соединение на запрос.
/// Пустая строка подключения — <see cref="Configured"/> = false, операции не вызываются (гейт в KB8).
/// </summary>
public sealed class KanbanBoardRepository(string connectionString, ISpellSchoolRepository? spellSchools = null) : IKanbanBoardRepository
{
    // ID проектов регрессии в БД project (см. tools/regression-import/epics.json и migrate-professions.py).
    private const int AbilitiesProjectId = 650;
    private const int ProfessionsProjectId = 2431;

    private readonly string _cs = connectionString;

    public bool Configured => !string.IsNullOrWhiteSpace(_cs);

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new MySqlConnection(_cs);
        await c.OpenAsync(ct);
        return c;
    }

    public async Task<IReadOnlyList<KanbanTesterTask>> GetTesterTasksAsync(
        uint testerGuid, KanbanTesterListKind kind = KanbanTesterListKind.General, CancellationToken ct = default)
    {
        // Талантов как отдельного проекта регрессии в БД пока нет — отдадим пусто (вкладка показывает заглушку).
        if (kind == KanbanTesterListKind.Talents) return [];

        var (extraWhere, args) = BuildKindFilter(kind, testerGuid);

        await using var c = await OpenAsync(ct);
        var rows = (await c.QueryAsync<TaskRow>(new CommandDefinition(
            "SELECT t.id AS Id, t.title AS Title, COALESCE(t.test_steps,'') AS TestSteps, " +
            "       COALESCE(t.expected_result,'') AS ExpectedResult, t.status AS Status " +
            "FROM project.kanban_ticket t " +
            "WHERE t.tester_guid = @g AND t.client_check = 1 AND t.status = 'Testing' " +
            extraWhere +
            " ORDER BY t.updated_at DESC", args, cancellationToken: ct))).ToList();

        // Для регрессионных вкладок дополняем результат полями SpellId/SchoolMask (нужны клиенту для иконки и сортировки).
        if (kind == KanbanTesterListKind.General || rows.Count == 0)
            return rows.ConvertAll(static r => new KanbanTesterTask(r.Id, r.Title, r.TestSteps, r.ExpectedResult, r.Status));

        var withSpellId = rows
            .Select(r => (Row: r, SpellId: KanbanTitleParser.TryParseSpellId(r.Title)))
            .ToList();
        var ids = withSpellId.Where(p => p.SpellId.HasValue).Select(p => p.SpellId!.Value).Distinct().ToArray();
        var schools = spellSchools is not null && ids.Length > 0
            ? await spellSchools.GetSchoolMasksAsync(ids, ct)
            : new Dictionary<int, int>();

        return withSpellId.ConvertAll(p =>
        {
            int? school = p.SpellId is { } id && schools.TryGetValue(id, out var s) ? s : null;
            return new KanbanTesterTask(p.Row.Id, p.Row.Title, p.Row.TestSteps, p.Row.ExpectedResult, p.Row.Status, p.SpellId, school);
        });
    }

    /// <summary>
    /// Условие WHERE и параметры для конкретной вкладки. Метки проверяются через EXISTS/NOT EXISTS по
    /// <c>kanban_ticket_label</c>+<c>kanban_label</c> (LOWER(name)) — паттерн из
    /// <c>KanbanRepository.ArchiveStaleDoneAsync</c> в Web.
    /// </summary>
    private static (string Where, object Args) BuildKindFilter(KanbanTesterListKind kind, uint testerGuid)
    {
        var args = new DynamicParameters();
        args.Add("g", testerGuid);
        switch (kind)
        {
            case KanbanTesterListKind.Abilities:
                args.Add("proj", AbilitiesProjectId);
                return (
                    " AND t.project_id = @proj" +
                    " AND EXISTS (SELECT 1 FROM project.kanban_ticket_label tl JOIN project.kanban_label l ON l.id = tl.label_id" +
                    "             WHERE tl.ticket_id = t.id AND LOWER(l.name) = 'regression')" +
                    " AND NOT EXISTS (SELECT 1 FROM project.kanban_ticket_label tl JOIN project.kanban_label l ON l.id = tl.label_id" +
                    "                 WHERE tl.ticket_id = t.id AND LOWER(l.name) = 'profession')", args);
            case KanbanTesterListKind.Professions:
                args.Add("proj", ProfessionsProjectId);
                return (
                    " AND t.project_id = @proj" +
                    " AND EXISTS (SELECT 1 FROM project.kanban_ticket_label tl JOIN project.kanban_label l ON l.id = tl.label_id" +
                    "             WHERE tl.ticket_id = t.id AND LOWER(l.name) = 'profession')", args);
            case KanbanTesterListKind.General:
            default:
                return (
                    " AND NOT EXISTS (SELECT 1 FROM project.kanban_ticket_label tl JOIN project.kanban_label l ON l.id = tl.label_id" +
                    "                 WHERE tl.ticket_id = t.id AND LOWER(l.name) = 'regression')", args);
        }
    }

    private sealed record TaskRow(int Id, string Title, string TestSteps, string ExpectedResult, string Status);

    public async Task<KanbanTicketRef?> GetTicketRefAsync(int ticketId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<KanbanTicketRef>(new CommandDefinition(
            "SELECT id AS Id, tester_guid AS TesterGuid, client_check AS ClientCheck, status AS Status " +
            "FROM project.kanban_ticket WHERE id = @id", new { id = ticketId }, cancellationToken: ct));
    }

    public async Task SetStatusAsync(int ticketId, string status, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE project.kanban_ticket SET status = @status WHERE id = @id",
            new { id = ticketId, status }, cancellationToken: ct));
    }

    public async Task AddCommentAsync(int ticketId, string author, string body, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "INSERT INTO project.kanban_comment (ticket_id, author, body) VALUES (@t, @a, @b)",
            new { t = ticketId, a = author, b = body }, cancellationToken: ct));
    }
}
