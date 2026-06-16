namespace AlexWoW.Database.Models;

/// <summary>Задача на тестирование для игрока-тестировщика (KB7): минимум для квест-окна аддона.</summary>
public sealed record KanbanTesterTask(int Id, string Title, string TestSteps, string ExpectedResult, string Status);

/// <summary>Справочная выжимка тикета для проверки прав/перехода статуса при сабмите из игры (KB7/KB8).</summary>
public sealed record KanbanTicketRef(int Id, uint? TesterGuid, bool ClientCheck, string Status);
