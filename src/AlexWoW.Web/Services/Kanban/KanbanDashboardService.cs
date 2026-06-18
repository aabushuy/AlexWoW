using AlexWoW.Web;
using Dapper;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace AlexWoW.Web.Services.Kanban;

/// <summary>
/// Дашборд канбана (новая страница /Dashboard). Источник истины — те же таблицы, что у доски
/// (<c>project.kanban_ticket</c>). Архивные тикеты в агрегаты не попадают.
/// </summary>
public sealed class KanbanDashboardService(IOptions<WebOptions> options)
{
    private readonly string _cs = options.Value.ProjectConnectionString;

    public bool Configured => !string.IsNullOrWhiteSpace(_cs);

    public sealed record Aggregate(int OpenTotal, int OpenBlocker, int OpenMajor, int OpenMinor,
                                    int OpenBugs, int OpenTasks, double PercentDone);

    public sealed record EpicProgress(int Id, string Title, int Done, int Open,
                                       IReadOnlyDictionary<string, int> ByStatus);

    public sealed record ProjectProgress(int Id, string Title, int Done, int Open,
                                          IReadOnlyDictionary<string, int> ByStatus,
                                          IReadOnlyList<EpicProgress> Epics);

    public sealed record TesterLoad(uint Guid, string Name, int InTesting);

    public sealed record DashboardData(
        Aggregate Summary,
        IReadOnlyList<ProjectProgress> Projects,
        IReadOnlyList<TesterLoad> Testers);

    /// <summary>Загрузить агрегаты. Один SELECT по тикетам + один SELECT по тестерам — всё остальное в памяти.</summary>
    public async Task<DashboardData> LoadAsync(CancellationToken ct)
    {
        if (!Configured) return Empty();

        await using var c = new MySqlConnection(_cs);
        await c.OpenAsync(ct);

        // Все НЕ архивные тикеты — для подсчёта прогресса, статусов и тестеров.
        var rows = (await c.QueryAsync<TicketRow>(new CommandDefinition(
            "SELECT id Id, title Title, type Type, status Status, priority Priority, " +
            "project_id ProjectId, epic_id EpicId, tester_guid TesterGuid " +
            "FROM project.kanban_ticket WHERE is_archive = 0", cancellationToken: ct))).ToList();

        // Имена тестеров — JOIN'нуть с alexwow_auth.characters по guid.
        var testerGuids = rows.Where(r => r is { Type: "Task" or "Bug", Status: "Testing", TesterGuid: not null })
            .Select(r => r.TesterGuid!.Value).Distinct().ToList();
        var testerNames = await LoadTesterNamesAsync(c, testerGuids, ct);

        return Build(rows, testerNames);
    }

    private static async Task<Dictionary<uint, string>> LoadTesterNamesAsync(
        MySqlConnection c, IReadOnlyCollection<uint> guids, CancellationToken ct)
    {
        if (guids.Count == 0) return [];
        // Cross-DB JOIN: канбан в `project`, персонажи в `alexwow_auth`. Прав у того же юзера достаточно.
        var rows = await c.QueryAsync<(uint Guid, string Name)>(new CommandDefinition(
            "SELECT guid Guid, name Name FROM alexwow_auth.characters WHERE guid IN @ids",
            new { ids = guids }, cancellationToken: ct));
        return rows.ToDictionary(r => r.Guid, r => r.Name);
    }

    private static DashboardData Build(List<TicketRow> rows, IReadOnlyDictionary<uint, string> testerNames)
    {
        // Сводные карточки: только листья (Task/Bug) — Project/Epic это контейнеры, их в проценте не считаем.
        var leaves = rows.Where(r => r.Type is "Task" or "Bug").ToList();
        var openLeaves = leaves.Where(r => r.Status != "Done").ToList();
        var doneLeaves = leaves.Count(r => r.Status == "Done");
        var summary = new Aggregate(
            OpenTotal: openLeaves.Count,
            OpenBlocker: openLeaves.Count(r => r.Priority == "Blocker"),
            OpenMajor: openLeaves.Count(r => r.Priority == "Major"),
            OpenMinor: openLeaves.Count(r => r.Priority == "Minor"),
            OpenBugs: openLeaves.Count(r => r.Type == "Bug"),
            OpenTasks: openLeaves.Count(r => r.Type == "Task"),
            PercentDone: leaves.Count == 0 ? 0.0 : (double)doneLeaves / leaves.Count);

        // Прогресс по проектам и эпикам.
        var projects = rows.Where(r => r.Type == "Project").OrderBy(r => r.Title).ToList();
        var epics = rows.Where(r => r.Type == "Epic").ToList();
        var leafByProject = leaves.GroupBy(r => r.ProjectId ?? 0).ToDictionary(g => g.Key, g => g.ToList());
        var leafByEpic = leaves.GroupBy(r => r.EpicId ?? 0).ToDictionary(g => g.Key, g => g.ToList());

        var projectList = new List<ProjectProgress>();
        foreach (var p in projects)
        {
            var pLeaves = leafByProject.GetValueOrDefault(p.Id, []);
            var (pDone, pOpen, pByStatus) = Tally(pLeaves);

            var epicList = new List<EpicProgress>();
            foreach (var e in epics.Where(e => e.ProjectId == p.Id).OrderBy(e => e.Title))
            {
                var eLeaves = leafByEpic.GetValueOrDefault(e.Id, []);
                var (eDone, eOpen, eByStatus) = Tally(eLeaves);
                epicList.Add(new EpicProgress(e.Id, e.Title, eDone, eOpen, eByStatus));
            }

            projectList.Add(new ProjectProgress(p.Id, p.Title, pDone, pOpen, pByStatus, epicList));
        }

        // Загрузка тестеров: сколько Task/Bug сейчас в Testing на каждого.
        var testers = leaves
            .Where(r => r.Status == "Testing" && r.TesterGuid is not null)
            .GroupBy(r => r.TesterGuid!.Value)
            .Select(g => new TesterLoad(g.Key, testerNames.GetValueOrDefault(g.Key, $"#{g.Key}"), g.Count()))
            .OrderByDescending(t => t.InTesting)
            .ToList();

        return new DashboardData(summary, projectList, testers);
    }

    private static (int Done, int Open, IReadOnlyDictionary<string, int> ByStatus) Tally(IReadOnlyList<TicketRow> rows)
    {
        var by = new Dictionary<string, int>();
        foreach (var s in KanbanVocab.Statuses) by[s] = 0;
        foreach (var r in rows)
            if (by.ContainsKey(r.Status)) by[r.Status]++;
        var done = by["Done"];
        return (done, rows.Count - done, by);
    }

    private static DashboardData Empty() => new(
        new Aggregate(0, 0, 0, 0, 0, 0, 0),
        Array.Empty<ProjectProgress>(),
        Array.Empty<TesterLoad>());

    private sealed record TicketRow
    {
        public int Id { get; init; }
        public string Title { get; init; } = "";
        public string Type { get; init; } = "";
        public string Status { get; init; } = "";
        public string Priority { get; init; } = "";
        public int? ProjectId { get; init; }
        public int? EpicId { get; init; }
        public uint? TesterGuid { get; init; }
    }
}
