using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace AlexWoW.Web.Services;

/// <summary>
/// Срез 1 дашборда: прогресс по БД <c>project</c> (трекинг механик/абилок/талантов/рас, перенос из docs/*.md).
/// Читает 4 таблицы, агрегирует статусы (✅ готово / 🟡 реализовано / ⬜ не сделано / ➖ вне этапа) в сводки
/// по доменам и разбивки (абилки/таланты по классам, механики по секциям, расы по расам). Read-only, Dapper.
/// </summary>
public sealed class ProjectDashboardService(IOptions<WebOptions> options, ILogger<ProjectDashboardService> logger)
{
    private readonly string _cs = options.Value.ProjectConnectionString;

    public bool Configured => !string.IsNullOrWhiteSpace(_cs);

    /// <summary>Счётчики по статусам. % = (готово + ½·в работе) / (всё кроме «вне этапа»).</summary>
    public sealed record StatusCounts(int Done, int Impl, int Todo, int Na)
    {
        public int Total => Done + Impl + Todo + Na;
        public int Tracked => Done + Impl + Todo;
        public int Percent => Tracked == 0 ? 0 : (int)Math.Round(100.0 * (Done + 0.5 * Impl) / Tracked);
    }

    public sealed record Group(string Name, StatusCounts Counts);
    public sealed record Domain(string Key, string Title, StatusCounts Counts, IReadOnlyList<Group> Groups);
    public sealed record DashboardData(IReadOnlyList<Domain> Domains);

    private sealed record Row(string GroupName, string Status, int Cnt);

    public async Task<DashboardData?> GetAsync(CancellationToken ct)
    {
        if (!Configured)
            return null;
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync(ct);
            var domains = new List<Domain>
            {
                await LoadAsync(conn, "Mechanics", "Механики", "section", ct),
                await LoadAsync(conn, "ClassAbilities", "Абилки классов", "class", ct),
                await LoadAsync(conn, "ClassTalents", "Таланты классов", "class", ct),
                await LoadAsync(conn, "RacesAbilities", "Расовые абилки", "race", ct),
            };
            return new DashboardData(domains);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Дашборд: не удалось прочитать БД project: {Msg}", ex.Message);
            return null;
        }
    }

    private static async Task<Domain> LoadAsync(MySqlConnection conn, string table, string title, string groupCol, CancellationToken ct)
    {
        // Имена таблиц/колонок — из белого списка (не из ввода), интерполяция безопасна.
        var sql = $"SELECT `{groupCol}` AS GroupName, status AS Status, COUNT(*) AS Cnt FROM project.`{table}` GROUP BY `{groupCol}`, status";
        var rows = (await conn.QueryAsync<Row>(new CommandDefinition(sql, cancellationToken: ct))).ToList();

        var groups = rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.GroupName) ? "—" : r.GroupName)
            .Select(g => new Group(g.Key, Count(g)))
            .OrderByDescending(g => g.Counts.Tracked)
            .ToList();
        return new Domain(table, title, Count(rows), groups);
    }

    private static StatusCounts Count(IEnumerable<Row> rows)
    {
        int done = 0, impl = 0, todo = 0, na = 0;
        foreach (var r in rows)
            switch (r.Status)
            {
                case "✅": done += r.Cnt; break;
                case "🟡": impl += r.Cnt; break;
                case "⬜": todo += r.Cnt; break;
                default: na += r.Cnt; break; // ➖ или пусто — вне этапа/без статуса
            }
        return new StatusCounts(done, impl, todo, na);
    }
}
