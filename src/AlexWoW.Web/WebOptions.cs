namespace AlexWoW.Web;

/// <summary>Конфигурация веб-панели (секция "Web" в appsettings.json).</summary>
public sealed class WebOptions
{
    public const string SectionName = "Web";

    /// <summary>Строка подключения к MySQL (БД alexwow_auth — те же аккаунты/персонажи, что и в игре).</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
