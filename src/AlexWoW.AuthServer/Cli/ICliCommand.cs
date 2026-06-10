namespace AlexWoW.AuthServer.Cli;

/// <summary>
/// CLI-команда auth-сервера (<c>dotnet run -- &lt;команда&gt; …</c>). Команды живут в том же
/// DI-контейнере, что и сервер, и используют те же репозитории — без дублирования сборки DAL
/// (рефактор S10, ранее — статики + CliRepository).
/// </summary>
internal interface ICliCommand
{
    /// <summary>Имя команды — первый аргумент командной строки (сравнение без учёта регистра).</summary>
    string Name { get; }

    /// <summary>Выполняет команду. Возвращает код выхода процесса (0 — успех).</summary>
    Task<int> RunAsync(string[] args, CancellationToken ct);
}
