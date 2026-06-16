using AlexWoW.Web;
using Microsoft.Extensions.Options;

namespace AlexWoW.Web.Services.Kanban;

/// <summary>
/// REST API канбан-доски (KB5) для Claude: JSON-эндпоинты поверх <see cref="KanbanService"/> (тот же источник
/// правды, что и web-UI). Авторизация — заголовок <c>X-Api-Token</c> == <c>Web:ApiToken</c> (homeserver-internal).
/// Вызывается curl'ом по SSH. Эндпоинты вне Razor-конвенций авторизации, поэтому гейт — собственный.
/// </summary>
public static class KanbanApi
{
    private sealed record MoveReq(string Status);
    private sealed record TesterReq(uint? TesterGuid, bool ClientCheck);
    private sealed record CommentReq(string? Author, string? Body);

    public static void MapKanbanApi(this WebApplication app)
    {
        var g = app.MapGroup("/api/kanban");

        g.MapGet("/tickets", async (KanbanService k, IOptions<WebOptions> o, HttpContext ctx,
            int? project, int? epic, string? status, string? type, uint? tester, CancellationToken ct) =>
        {
            if (!Authorized(ctx, o)) return Results.Unauthorized();
            var list = await k.ListAsync(new KanbanFilter
            {
                ProjectId = project, EpicId = epic, Status = status, Type = type, TesterGuid = tester,
            }, ct);
            return Results.Ok(list);
        });

        g.MapGet("/tickets/{id:int}", async (int id, KanbanService k, IOptions<WebOptions> o, HttpContext ctx, CancellationToken ct) =>
        {
            if (!Authorized(ctx, o)) return Results.Unauthorized();
            var (ticket, comments) = await k.GetAsync(id, ct);
            return ticket is null ? Results.NotFound() : Results.Ok(new { ticket, comments });
        });

        g.MapPost("/tickets", async (KanbanTicket body, KanbanService k, IOptions<WebOptions> o, HttpContext ctx, CancellationToken ct) =>
        {
            if (!Authorized(ctx, o)) return Results.Unauthorized();
            try { return Results.Ok(new { id = await k.CreateAsync(body, ct) }); }
            catch (KanbanValidationException e) { return Results.BadRequest(new { error = e.Message }); }
        });

        g.MapPatch("/tickets/{id:int}", async (int id, KanbanTicket body, KanbanService k, IOptions<WebOptions> o, HttpContext ctx, CancellationToken ct) =>
        {
            if (!Authorized(ctx, o)) return Results.Unauthorized();
            try { await k.UpdateAsync(body with { Id = id }, ct); return Results.Ok(new { id }); }
            catch (KanbanValidationException e) { return Results.BadRequest(new { error = e.Message }); }
        });

        g.MapPost("/tickets/{id:int}/move", async (int id, MoveReq body, KanbanService k, IOptions<WebOptions> o, HttpContext ctx, CancellationToken ct) =>
        {
            if (!Authorized(ctx, o)) return Results.Unauthorized();
            try { await k.MoveAsync(id, body.Status, ct); return Results.Ok(new { id, body.Status }); }
            catch (KanbanValidationException e) { return Results.BadRequest(new { error = e.Message }); }
        });

        g.MapPost("/tickets/{id:int}/tester", async (int id, TesterReq body, KanbanService k, IOptions<WebOptions> o, HttpContext ctx, CancellationToken ct) =>
        {
            if (!Authorized(ctx, o)) return Results.Unauthorized();
            await k.SetTesterAsync(id, body.TesterGuid, body.ClientCheck, ct);
            return Results.Ok(new { id });
        });

        g.MapPost("/tickets/{id:int}/comments", async (int id, CommentReq body, KanbanService k, IOptions<WebOptions> o, HttpContext ctx, CancellationToken ct) =>
        {
            if (!Authorized(ctx, o)) return Results.Unauthorized();
            try { return Results.Ok(new { id = await k.CommentAsync(id, body.Author ?? "claude", body.Body ?? "", ct) }); }
            catch (KanbanValidationException e) { return Results.BadRequest(new { error = e.Message }); }
        });
    }

    private static bool Authorized(HttpContext ctx, IOptions<WebOptions> o)
    {
        var token = o.Value.ApiToken;
        return !string.IsNullOrEmpty(token)
            && string.Equals(ctx.Request.Headers["X-Api-Token"].ToString(), token, StringComparison.Ordinal);
    }
}
