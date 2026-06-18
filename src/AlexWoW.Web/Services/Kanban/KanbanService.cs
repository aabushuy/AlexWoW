namespace AlexWoW.Web.Services.Kanban;

/// <summary>
/// Бизнес-логика канбан-доски (KB2): валидация перечислений и **дерева** (Project→Epic→Task/Bug) поверх
/// <see cref="KanbanRepository"/>. Единый источник правды для Web-UI (KB3/KB4) и REST API (KB5).
/// </summary>
public sealed class KanbanService(KanbanRepository repo)
{
    public bool Configured => repo.Configured;

    public async Task<IReadOnlyList<KanbanTicket>> ListAsync(KanbanFilter f, CancellationToken ct)
    {
        var rows = await repo.ListAsync(f, ct);
        return await AttachLabelsAsync(rows, ct);
    }

    /// <summary>Проекты — это «оглавление», их архивный фильтр не касается (по умолчанию всегда все).</summary>
    public Task<IReadOnlyList<KanbanTicket>> ProjectsAsync(CancellationToken ct) =>
        repo.ListAsync(new KanbanFilter { Type = "Project", IncludeArchived = true }, ct);

    public Task<IReadOnlyList<KanbanTicket>> EpicsAsync(int projectId, CancellationToken ct) =>
        repo.ListAsync(new KanbanFilter { Type = "Epic", ProjectId = projectId, IncludeArchived = true }, ct);

    public Task<IReadOnlyList<KanbanTicket>> AllEpicsAsync(CancellationToken ct) =>
        repo.ListAsync(new KanbanFilter { Type = "Epic", IncludeArchived = true }, ct);

    public async Task<(KanbanTicket? Ticket, IReadOnlyList<KanbanComment> Comments)> GetAsync(int id, CancellationToken ct)
    {
        var t = await repo.GetAsync(id, ct);
        if (t is null) return (null, []);
        var labels = await repo.LabelsForAsync(id, ct);
        return (t with { Labels = labels }, await repo.CommentsAsync(id, ct));
    }

    public async Task<int> CreateAsync(KanbanTicket t, CancellationToken ct)
    {
        t = await NormalizeTreeAsync(t, ct);
        var id = await repo.CreateAsync(t, ct);
        await repo.SetLabelsAsync(id, t.Labels, ct);
        return id;
    }

    public async Task UpdateAsync(KanbanTicket t, CancellationToken ct)
    {
        if (await repo.GetAsync(t.Id, ct) is null)
            throw new KanbanValidationException($"Тикет #{t.Id} не найден");
        t = await NormalizeTreeAsync(t, ct);
        EnsureTesterReady(t.TesterGuid, t.TestSteps, t.ExpectedResult); // нельзя назначить тестера без шагов/ожидаемого
        await repo.UpdateAsync(t, ct);
        await repo.SetLabelsAsync(t.Id, t.Labels, ct);
    }

    public Task SetArchiveAsync(int id, bool archive, CancellationToken ct) => repo.SetArchiveAsync(id, archive, ct);

    public Task<IReadOnlyList<string>> AllLabelsAsync(CancellationToken ct) => repo.AllLabelsAsync(ct);

    /// <summary>Подгрузить метки одной пачкой и положить их в Labels каждого тикета.</summary>
    private async Task<IReadOnlyList<KanbanTicket>> AttachLabelsAsync(IReadOnlyList<KanbanTicket> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return rows;
        var ids = rows.Select(static r => r.Id).ToList();
        var labels = await repo.LabelsByTicketAsync(ids, ct);
        var result = new List<KanbanTicket>(rows.Count);
        foreach (var t in rows)
            result.Add(labels.TryGetValue(t.Id, out var ls) ? t with { Labels = ls } : t);
        return result;
    }

    public async Task MoveAsync(int id, string status, CancellationToken ct)
    {
        if (!KanbanVocab.IsStatus(status))
            throw new KanbanValidationException($"Недопустимый статус: {status}");
        await repo.SetStatusAsync(id, status, ct);
    }

    public async Task SetTesterAsync(int id, uint? testerGuid, bool clientCheck, CancellationToken ct)
    {
        if (testerGuid is not null)
        {
            var t = await repo.GetAsync(id, ct) ?? throw new KanbanValidationException($"Тикет #{id} не найден");
            EnsureTesterReady(testerGuid, t.TestSteps, t.ExpectedResult);
        }
        await repo.SetTesterAsync(id, testerGuid, clientCheck, ct);
    }

    /// <summary>Нельзя назначить тестировщика, если не заполнены «Шаги тестирования» и «Ожидаемый результат».</summary>
    private static void EnsureTesterReady(uint? testerGuid, string? testSteps, string? expectedResult)
    {
        if (testerGuid is null) return;
        if (string.IsNullOrWhiteSpace(testSteps))
            throw new KanbanValidationException("Перед назначением тестировщика заполните «Шаги тестирования»");
        if (string.IsNullOrWhiteSpace(expectedResult))
            throw new KanbanValidationException("Перед назначением тестировщика заполните «Ожидаемый результат»");
    }

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
