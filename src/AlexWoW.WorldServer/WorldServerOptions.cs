namespace AlexWoW.WorldServer;

/// <summary>Конфигурация world-сервера (секция "WorldServer" в appsettings.json).</summary>
public sealed class WorldServerOptions
{
    public const string SectionName = "WorldServer";

    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Порт world-сервера. Клиент WoW ожидает 8085.</summary>
    public int Port { get; set; } = 8085;

    /// <summary>Строка подключения к MySQL (база auth — для проверки session key).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Строка подключения к базе мира (дамп CMaNGOS: creature, creature_template …). Пусто = без БД мира.</summary>
    public string WorldConnectionString { get; set; } = string.Empty;

    /// <summary>Ожидаемый build клиента.</summary>
    public ushort ExpectedBuild { get; set; } = 12340;
}
