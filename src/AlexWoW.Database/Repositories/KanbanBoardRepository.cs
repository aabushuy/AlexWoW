using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;
using MySqlConnector;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// Dapper-доступ world-сервера к канбан-доске в БД <c>project</c> (KB7). Короткоживущее соединение на запрос.
/// Пустая строка подключения — <see cref="Configured"/> = false, операции не вызываются (гейт в KB8).
/// </summary>
public sealed class KanbanBoardRepository(string connectionString) : IKanbanBoardRepository
{
    private readonly string _cs = connectionString;

    public bool Configured => !string.IsNullOrWhiteSpace(_cs);

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new MySqlConnection(_cs);
        await c.OpenAsync(ct);
        return c;
    }

    public async Task<IReadOnlyList<KanbanTesterTask>> GetTesterTasksAsync(uint testerGuid, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        var rows = await c.QueryAsync<KanbanTesterTask>(new CommandDefinition(
            "SELECT id AS Id, title AS Title, COALESCE(test_steps,'') AS TestSteps, " +
            "COALESCE(expected_result,'') AS ExpectedResult, status AS Status " +
            "FROM project.kanban_ticket WHERE tester_guid = @g AND client_check = 1 AND status = 'Testing' " +
            "ORDER BY updated_at DESC", new { g = testerGuid }, cancellationToken: ct));
        return rows.ToList();
    }

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
