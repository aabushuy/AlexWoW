using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Доступ world-сервера к канбан-доске в БД <c>project</c> (KB7): чтение задач на тестирование для
/// персонажа-тестировщика и сабмит результата из игры (переход статуса + комментарий). Потребитель —
/// addon-протокол (KB8). Отдельно от Web-репозитория: world не зависит от Web.
/// </summary>
public interface IKanbanBoardRepository
{
    /// <summary>Настроена ли строка подключения к БД <c>project</c> (иначе функционал выключен).</summary>
    bool Configured { get; }

    /// <summary>
    /// Задачи на тестирование для персонажа (tester_guid=guid, client_check=1, статус Testing) с фильтром
    /// по вкладке аддона: <see cref="KanbanTesterListKind.General"/> — нерегрессионная очередь (старое KB8),
    /// остальные значения — regression-задачи нужного типа. Для regression-вкладок Spell-поля результата
    /// заполнены (KB14): <c>SpellId</c> из title, <c>SchoolMask</c> — отдельным запросом по <c>spell_template</c>.
    /// </summary>
    Task<IReadOnlyList<KanbanTesterTask>> GetTesterTasksAsync(
        uint testerGuid, KanbanTesterListKind kind = KanbanTesterListKind.General, CancellationToken ct = default);

    /// <summary>Выжимка тикета (для проверки прав тестировщика и текущего статуса перед сабмитом).</summary>
    Task<KanbanTicketRef?> GetTicketRefAsync(int ticketId, CancellationToken ct = default);

    /// <summary>Сменить статус тикета.</summary>
    Task SetStatusAsync(int ticketId, string status, CancellationToken ct = default);

    /// <summary>Добавить комментарий к тикету.</summary>
    Task AddCommentAsync(int ticketId, string author, string body, CancellationToken ct = default);
}
