namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Репозиторий key-value настроек сервера (таблица <c>server_setting</c>, БД alexwow_auth).
/// Чтение — для веб-панели (стоимости смены расы/пола и т.п.). M8.6.
/// </summary>
public interface ISettingRepository
{
    /// <summary>Значение настройки по ключу или null, если ключа нет.</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Все настройки (ключ → значение).</summary>
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default);
}
