namespace AlexWoW.AuthServer;

/// <summary>Конфигурация логин-сервера (секция "AuthServer" в appsettings.json).</summary>
public sealed class AuthServerOptions
{
    public const string SectionName = "AuthServer";

    /// <summary>Адрес для прослушивания. 0.0.0.0 — все интерфейсы (нужно для доступа из сети/докера).</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Порт логин-сервера. Клиент WoW ожидает 3724.</summary>
    public int Port { get; set; } = 3724;

    /// <summary>Строка подключения к MySQL (база auth).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Параметры реалма по умолчанию, который создаётся при первом запуске.</summary>
    public DefaultRealmOptions DefaultRealm { get; set; } = new();
}

public sealed class DefaultRealmOptions
{
    public string Name { get; set; } = "AlexWoW";
    /// <summary>IP world-сервера, который увидит клиент. Должен быть доступен с машины игрока.</summary>
    public string Address { get; set; } = "127.0.0.1";
    public ushort Port { get; set; } = 8085;
    public byte Type { get; set; } = 0;
    public byte Flags { get; set; } = 0;
    public byte Timezone { get; set; } = 1;
    public float Population { get; set; } = 0f;
}
