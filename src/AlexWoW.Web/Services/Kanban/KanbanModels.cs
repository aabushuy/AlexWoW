namespace AlexWoW.Web.Services.Kanban;

/// <summary>Тикет канбан-доски (KB2). Project/Epic/Task/Bug — всё это тикеты (поле Type); дерево Project→Epic→Task.</summary>
public sealed record KanbanTicket
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public string? TestSteps { get; init; }
    public string? ExpectedResult { get; init; }
    public string Priority { get; init; } = "Minor";
    public string Type { get; init; } = "Task";
    public string Status { get; init; } = "Backlog";
    public int? EpicId { get; init; }
    public int? ProjectId { get; init; }
    public string Assignee { get; init; } = "";
    public uint? TesterGuid { get; init; }
    public bool ClientCheck { get; init; }
    public bool IsArchive { get; init; }
    public DateTime? DoneAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>Метки тикета (Jira-style). Заполняется сервисом из таблицы kanban_ticket_label.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];
}

/// <summary>Комментарий тикета (лента сортируется по CreatedAt ASC).</summary>
public sealed record KanbanComment
{
    public int Id { get; init; }
    public int TicketId { get; init; }
    public string Author { get; init; } = "";
    public string Body { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

/// <summary>Фильтр выборки тикетов (любое поле null — без ограничения).</summary>
public sealed record KanbanFilter
{
    public int? ProjectId { get; init; }
    public int? EpicId { get; init; }
    public string? Status { get; init; }
    public string? Type { get; init; }
    public uint? TesterGuid { get; init; }
    public bool? ClientCheck { get; init; }

    /// <summary>true — отдавать и архивные тикеты (is_archive=1) вперемешку. По умолчанию архив скрыт.</summary>
    public bool IncludeArchived { get; init; }

    /// <summary>Имена меток (AND-семантика, как в Jira). null/пусто — фильтр не применяется.</summary>
    public IReadOnlyList<string>? Labels { get; init; }
}

/// <summary>Допустимые значения перечислений (совпадают со строками ENUM в БД и подписями в UI).</summary>
public static class KanbanVocab
{
    public static readonly IReadOnlyList<string> Statuses =
        ["Backlog", "Ready to Implementation", "In Progress", "Testing", "Done"];
    public static readonly IReadOnlyList<string> Priorities = ["Blocker", "Major", "Minor"];
    public static readonly IReadOnlyList<string> Types = ["Task", "Bug", "Epic", "Project"];

    public static bool IsStatus(string? s) => s is not null && Statuses.Contains(s);
    public static bool IsPriority(string? s) => s is not null && Priorities.Contains(s);
    public static bool IsType(string? s) => s is not null && Types.Contains(s);
}

/// <summary>Нарушение правил доски (дерево/перечисления) — для аккуратной обработки в API/UI.</summary>
public sealed class KanbanValidationException(string message) : Exception(message);
