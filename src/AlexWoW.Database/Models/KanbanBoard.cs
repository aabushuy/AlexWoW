namespace AlexWoW.Database.Models;

/// <summary>
/// Задача на тестирование для игрока-тестировщика (KB7). Поля <c>SpellId</c>/<c>SchoolMask</c> заполняются
/// только для regression-тикетов (вкладки Абилки/Профессия/Таланты), где title парсится по
/// <see cref="KanbanTitleParser"/> и затем подгружается <c>SchoolMask</c> из <c>mangos.spell_template</c>.
/// Для общей вкладки оба поля = <see langword="null"/>.
/// </summary>
public sealed record KanbanTesterTask(
    int Id, string Title, string TestSteps, string ExpectedResult, string Status,
    int? SpellId = null, int? SchoolMask = null);

/// <summary>Справочная выжимка тикета для проверки прав/перехода статуса при сабмите из игры (KB7/KB8).</summary>
public sealed record KanbanTicketRef(int Id, uint? TesterGuid, bool ClientCheck, string Status);

/// <summary>
/// Разновидность списка задач для аддона <c>AlexQATester</c> (KB14): вкладки меню тестировщика.
/// Фильтр на сервере определяет, по какому проекту/меткам формируется выборка.
/// </summary>
public enum KanbanTesterListKind
{
    /// <summary>Общая (нерегрессионная) очередь — старое поведение KB8.</summary>
    General,
    /// <summary>Regression «Абилки»: project=650, label <c>regression</c>, без метки <c>profession</c>.</summary>
    Abilities,
    /// <summary>Regression «Таланты»: отдельного проекта ещё нет — пустой список.</summary>
    Talents,
    /// <summary>Regression «Профессия»: project=2431, label <c>profession</c>.</summary>
    Professions,
}
