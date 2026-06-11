using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AlexWoW.Web.Services;

/// <summary>
/// Заведение тикетов в Vikunja по аномалиям сессии захвата (M12 Spell QA, SQA-6). Один тикет на сессию.
/// Настройки — секция <c>Web:Vikunja</c> (URL/токен/проект). Если не настроено — <see cref="Configured"/>
/// false, на странице остаётся копи-фоллбэк (тело тикета для ручного заведения).
/// </summary>
public sealed class VikunjaTicketService(IOptions<WebOptions> options, ILogger<VikunjaTicketService> logger)
{
    private readonly WebOptions.VikunjaOptions _opt = options.Value.Vikunja;

    public bool Configured => _opt.Configured;

    /// <summary>Создаёт задачу в проекте Vikunja, возвращает её id; null — не настроено или ошибка.</summary>
    public async Task<uint?> CreateTaskAsync(string title, string description, CancellationToken ct)
    {
        if (!_opt.Configured)
            return null;
        try
        {
            using var handler = new HttpClientHandler();
            if (!_opt.VerifySsl) // домашний сервер с самоподписанным сертификатом
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var http = new HttpClient(handler) { BaseAddress = new Uri(_opt.BaseUrl) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opt.Token);

            var payload = JsonSerializer.Serialize(new { title, description });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            // Vikunja: создание задачи в проекте — PUT /api/v1/projects/{id}/tasks.
            using var resp = await http.PutAsync($"/api/v1/projects/{_opt.ProjectId}/tasks", content, ct);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var id)
                ? (uint)id
                : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Vikunja: не удалось завести тикет '{Title}': {Msg}", title, ex.Message);
            return null;
        }
    }
}
