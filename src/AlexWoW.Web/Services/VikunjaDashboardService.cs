using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AlexWoW.Web.Services;

/// <summary>
/// Срез 2 дашборда: сводка по трекеру Vikunja — доменные проекты <c>P01..P40</c>. По каждому: название+ссылка,
/// опкоды и вес (из описания проекта), число задач, статус. Переиспользует настройки <c>Web:Vikunja</c>
/// (URL/токен/самоподписанный TLS), кэш в памяти ~60 с (иначе 40+ запросов на каждую загрузку страницы).
/// </summary>
public sealed partial class VikunjaDashboardService(IOptions<WebOptions> options, ILogger<VikunjaDashboardService> logger)
{
    private readonly WebOptions.VikunjaOptions _opt = options.Value.Vikunja;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<TrackerProject>? _cache;
    private DateTimeOffset _cachedAt;

    public bool Configured => !string.IsNullOrWhiteSpace(_opt.BaseUrl) && !string.IsNullOrWhiteSpace(_opt.Token);

    public sealed record TrackerProject(int Id, string Name, string Url, string Opcodes, string Weight, int Tasks, int Done, string Status);

    [GeneratedRegex(@"^P\d\d\b")] private static partial Regex PProject();
    [GeneratedRegex(@"вес\s+([^\s·]+)")] private static partial Regex WeightRe();
    [GeneratedRegex(@"опкоды\s+([^.\n]+)")] private static partial Regex OpcodesRe();
    [GeneratedRegex(@"[✅🟡⬜]", RegexOptions.None, "en")] private static partial Regex StatusRe();

    public async Task<IReadOnlyList<TrackerProject>?> GetAsync(CancellationToken ct)
    {
        if (!Configured)
            return null;
        if (_cache is not null && DateTimeOffset.UtcNow - _cachedAt < CacheTtl)
            return _cache;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cache is not null && DateTimeOffset.UtcNow - _cachedAt < CacheTtl)
                return _cache;
            _cache = await FetchAsync(ct);
            _cachedAt = DateTimeOffset.UtcNow;
            return _cache;
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<TrackerProject>> FetchAsync(CancellationToken ct)
    {
        using var handler = new HttpClientHandler();
        if (!_opt.VerifySsl)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        using var http = new HttpClient(handler) { BaseAddress = new Uri(_opt.BaseUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opt.Token);

        var projectsJson = await http.GetStringAsync("/api/v1/projects?per_page=200", ct);
        using var doc = JsonDocument.Parse(projectsJson);

        var baseUrl = _opt.BaseUrl.TrimEnd('/');
        var domain = new List<(int Id, string Title, string Desc)>();
        foreach (var p in doc.RootElement.EnumerateArray())
        {
            var title = p.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            if (!PProject().IsMatch(title))
                continue;
            var id = p.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
            var desc = p.TryGetProperty("description", out var dEl) ? dEl.GetString() ?? "" : "";
            domain.Add((id, title, desc));
        }

        var tasks = await Task.WhenAll(domain.Select(async d =>
        {
            var (tasksCount, done) = await TaskCountsAsync(http, d.Id, ct);
            return new TrackerProject(
                d.Id, d.Title, $"{baseUrl}/projects/{d.Id}",
                Match(OpcodesRe(), d.Desc, "—"), Match(WeightRe(), d.Desc, "—"),
                tasksCount, done, StatusRe().Match(d.Desc) is { Success: true } m ? m.Value : "");
        }));

        return tasks.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
    }

    private async Task<(int Total, int Done)> TaskCountsAsync(HttpClient http, int projectId, CancellationToken ct)
    {
        try
        {
            var json = await http.GetStringAsync($"/api/v1/projects/{projectId}/tasks?per_page=250", ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return (0, 0);
            int total = 0, done = 0;
            foreach (var task in doc.RootElement.EnumerateArray())
            {
                total++;
                if (task.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True)
                    done++;
            }
            return (total, done);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Vikunja: не удалось получить задачи проекта {Id}", projectId);
            return (0, 0);
        }
    }

    private static string Match(Regex re, string input, string fallback)
    {
        var m = re.Match(input);
        return m.Success ? m.Groups[1].Value.Trim() : fallback;
    }
}
