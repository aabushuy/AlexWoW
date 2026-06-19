namespace AlexWoW.Web;

/// <summary>Конфигурация веб-панели (секция "Web" в appsettings.json).</summary>
public sealed class WebOptions
{
    public const string SectionName = "Web";

    /// <summary>Строка подключения к MySQL (БД alexwow_auth — те же аккаунты/персонажи, что и в игре).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Строка подключения к БД мира (<c>mangos</c>, дамп CMaNGOS) — поиск предметов в админке.</summary>
    public string WorldConnectionString { get; set; } = string.Empty;

    /// <summary>Строка подключения к БД <c>project</c> (канбан-доска + новый дашборд). Пусто — функционал скрыт.</summary>
    public string ProjectConnectionString { get; set; } = string.Empty;

    /// <summary>Токен REST API канбан-доски (KB5, заголовок <c>X-Api-Token</c>). Пусто — API отключён.</summary>
    public string ApiToken { get; set; } = string.Empty;
}
