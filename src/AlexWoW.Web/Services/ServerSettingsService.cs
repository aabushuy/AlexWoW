using AlexWoW.Database;
using AlexWoW.Database.Abstractions;

namespace AlexWoW.Web.Services;

/// <summary>
/// Чтение настроек сервера для веб-панели (стоимости смены расы/пола, M8.6). Поверх
/// <see cref="ISettingRepository"/>; при отсутствии/битом значении ключа — дефолт из
/// <see cref="ServerSettingKeys.Defaults"/>.
/// </summary>
public sealed class ServerSettingsService(ISettingRepository settings)
{
    /// <summary>Стоимость смены расы (в золоте).</summary>
    public Task<uint> RaceChangeCostGoldAsync(CancellationToken ct = default) =>
        GetGoldAsync(ServerSettingKeys.RaceChangeCostGold, ct);

    /// <summary>Стоимость смены пола (в золоте).</summary>
    public Task<uint> GenderChangeCostGoldAsync(CancellationToken ct = default) =>
        GetGoldAsync(ServerSettingKeys.GenderChangeCostGold, ct);

    private async Task<uint> GetGoldAsync(string key, CancellationToken ct)
    {
        var raw = await settings.GetAsync(key, ct);
        if (!string.IsNullOrWhiteSpace(raw) && uint.TryParse(raw, out var gold))
            return gold;
        // Фолбэк на дефолт, если ключа нет или значение не парсится.
        return ServerSettingKeys.Defaults.TryGetValue(key, out var def) && uint.TryParse(def, out var d) ? d : 0;
    }
}
