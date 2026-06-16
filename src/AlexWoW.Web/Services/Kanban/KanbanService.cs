namespace AlexWoW.Web.Services.Kanban;

/// <summary>
/// Бизнес-логика канбан-доски (KB2): валидация перечислений и **дерева** (Project→Epic→Task/Bug) поверх
/// <see cref="KanbanRepository"/>. Единый источник правды для Web-UI (KB3/KB4) и REST API (KB5).
/// </summary>
public sealed class KanbanService(KanbanRepository repo)
{
    public bool Configured => repo.Configured;

    public Task<IReadOnlyList<KanbanTicket>> ListAsync(KanbanFilter f, CancellationToken ct) => repo.ListAsync(f, ct);

    public Task<IReadOnlyList<KanbanTicket>> ProjectsAsync(CancellationToken ct) =>
        repo.ListAsync(new KanbanFilter { Type = "Project" }, ct);

    public Task<IReadOnlyList<KanbanTicket>> EpicsAsync(int projectId, CancellationToken ct) =>
        repo.ListAsync(new KanbanFilter { Type = "Epic", ProjectId = projectId }, ct);

    public Task<IReadOnlyList<KanbanTicket>> AllEpicsAsync(CancellationToken ct) =>
        repo.ListAsync(new KanbanFilter { Type = "Epic" }, ct);

    public async Task<(KanbanTicket? Ticket, IReadOnlyList<KanbanComment> Comments)> GetAsync(int id, CancellationToken ct)
    {
        var t = await repo.GetAsync(id, ct);
        return t is null ? (null, []) : (t, await repo.CommentsAsync(id, ct));
    }

    public async Task<int> CreateAsync(KanbanTicket t, CancellationToken ct)
    {
        t = await NormalizeTreeAsync(t, ct);
        return await repo.CreateAsync(t, ct);
    }

    public async Task UpdateAsync(KanbanTicket t, CancellationToken ct)
    {
        if (await repo.GetAsync(t.Id, ct) is null)
            throw new KanbanValidationException($"Тикет #{t.Id} не найден");
        t = await NormalizeTreeAsync(t, ct);
        await repo.UpdateAsync(t, ct);
    }

    public async Task MoveAsync(int id, string status, CancellationToken ct)
    {
        if (!KanbanVocab.IsStatus(status))
            throw new KanbanValidationException($"Недопустимый статус: {status}");
        await repo.SetStatusAsync(id, status, ct);
    }

    public Task SetTesterAsync(int id, uint? testerGuid, bool clientCheck, CancellationToken ct) =>
        repo.SetTesterAsync(id, testerGuid, clientCheck, ct);

    public async Task<int> CommentAsync(int id, string author, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new KanbanValidationException("Пустой комментарий");
        return await repo.AddCommentAsync(id, author, body, ct);
    }

    /// <summary>Проверяет перечисления и приводит дерево к правилам; возвращает нормализованный тикет.</summary>
    private async Task<KanbanTicket> NormalizeTreeAsync(KanbanTicket t, CancellationToken ct)
    {
        if (!KanbanVocab.IsType(t.Type)) throw new KanbanValidationException($"Недопустимый тип: {t.Type}");
        if (!KanbanVocab.IsPriority(t.Priority)) throw new KanbanValidationException($"Недопустимый приоритет: {t.Priority}");
        if (!KanbanVocab.IsStatus(t.Status)) throw new KanbanValidationException($"Недопустимый статус: {t.Status}");
        if (string.IsNullOrWhiteSpace(t.Title)) throw new KanbanValidationException("Пустой заголовок");

        switch (t.Type)
        {
            case "Project":
                // Проект — корень: ни эпика, ни проекта-родителя. Содержит только эпики (правило применяется к детям).
                return t with { EpicId = null, ProjectId = null };

            case "Epic":
                if (t.ProjectId is not { } pid)
                    throw new KanbanValidationException("Эпик должен принадлежать проекту (project_id)");
                var proj = await repo.GetAsync(pid, ct);
                if (proj is null || proj.Type != "Project")
                    throw new KanbanValidationException($"project_id={pid} не является проектом");
                return t with { EpicId = null };

            default: // Task / Bug
                if (t.EpicId is not { } eid)
                    throw new KanbanValidationException("Задача/баг должны принадлежать эпику (epic_id)");
                var epic = await repo.GetAsync(eid, ct);
                if (epic is null || epic.Type != "Epic")
                    throw new KanbanValidationException($"epic_id={eid} не является эпиком");
                // project_id наследуется от эпика (нельзя заводить задачи прямо в проекте).
                return t with { ProjectId = epic.ProjectId };
        }
    }
}
