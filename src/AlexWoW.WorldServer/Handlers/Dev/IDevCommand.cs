namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// Одна dev-команда (Command-паттерн). Команды авто-регистрируются DI-сканом сборки
/// (<see cref="DevCommandRegistration"/>) и собираются в <see cref="DevCommandRegistry"/> — добавить команду =
/// добавить класс-реализацию, центральный switch не нужен (как реестр опкодов <c>WorldPacketRouter</c>).
/// Зависимости — через конструктор; гейт <c>is_admin</c> и парсинг — в <see cref="DevCommandDispatcher"/>.
/// </summary>
internal interface IDevCommand
{
    /// <summary>Имя команды и алиасы (без точки, нижним регистром). Первое — основное.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>Сегмент для <c>.help</c> (например <c>.level N</c>); пустая строка — не показывать.</summary>
    string Help { get; }

    /// <summary>Порядок в <c>.help</c> (по возрастанию).</summary>
    int Order { get; }

    /// <summary>Требуется нахождение персонажа в мире (иначе диспетчер ответит «Доступно только в мире»).</summary>
    bool RequiresWorld { get; }

    /// <summary>Выполнить команду. <paramref name="ctx"/> несёт сессию, аргументы и ответ в чат.</summary>
    Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct);
}
