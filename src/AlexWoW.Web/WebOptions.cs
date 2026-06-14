namespace AlexWoW.Web;

/// <summary>Конфигурация веб-панели (секция "Web" в appsettings.json).</summary>
public sealed class WebOptions
{
    public const string SectionName = "Web";

    /// <summary>Строка подключения к MySQL (БД alexwow_auth — те же аккаунты/персонажи, что и в игре).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Строка подключения к БД <c>project</c> (трекинг прогресса для дашборда). Пусто — срез БД скрыт.</summary>
    public string ProjectConnectionString { get; set; } = string.Empty;

    /// <summary>Интеграция с Vikunja (M12 Spell QA — заведение тикета по аномалиям сессии). Пусто — выключено.</summary>
    public VikunjaOptions Vikunja { get; set; } = new();

    /// <summary>Настройки Vikunja REST API (секция "Web:Vikunja"). Токен/URL задаются в деплое, не в репозитории.</summary>
    public sealed class VikunjaOptions
    {
        /// <summary>База API, напр. https://tasks.home.srv.</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>API-токен (Bearer).</summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>Проект Vikunja, куда заводить тикеты Spell QA.</summary>
        public uint ProjectId { get; set; }

        /// <summary>Проверять TLS-сертификат (false — самоподписанный домашний сервер).</summary>
        public bool VerifySsl { get; set; } = true;

        /// <summary>Интеграция полностью настроена (иначе кнопка скрыта, доступен только копи-фоллбэк).</summary>
        public bool Configured => !string.IsNullOrWhiteSpace(BaseUrl)
            && !string.IsNullOrWhiteSpace(Token) && ProjectId > 0;
    }
}
