namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>server_setting</c> (БД alexwow_auth): key-value настройки сервера.</summary>
public sealed class ServerSetting
{
    public string Key { get; set; } = null!;     // varchar(64), PK
    public string Value { get; set; } = null!;    // varchar(255)
}
