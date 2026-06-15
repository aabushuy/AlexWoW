using System.Globalization;

namespace AlexWoW.Web.Services;

/// <summary>
/// Карта displayid → имя иконки предмета (извлечено офлайн из клиента 3.3.5a:
/// ItemDisplayInfo.dbc + BLP→PNG, см. tools/MapExtractor команда iconmap). PNG лежат в
/// wwwroot/icons/, карта — wwwroot/icons/_map.tsv. Загружается один раз при старте.
/// </summary>
public sealed class ItemIconService
{
    /// <summary>Иконка-заглушка, если для предмета нет соответствия (всегда присутствует в наборе).</summary>
    public const string FallbackIcon = "inv_misc_questionmark";

    private readonly Dictionary<uint, string> _byDisplayId = [];

    public ItemIconService(IWebHostEnvironment env, ILogger<ItemIconService> log)
    {
        var path = Path.Combine(env.WebRootPath ?? "wwwroot", "icons", "_map.tsv");
        if (!File.Exists(path))
        {
            log.LogWarning("Карта иконок не найдена: {Path} — иконки будут заглушками", path);
            return;
        }
        foreach (var line in File.ReadLines(path))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            if (uint.TryParse(line.AsSpan(0, tab), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                _byDisplayId[id] = line[(tab + 1)..].Trim();
        }
        log.LogInformation("Загружено иконок предметов: {Count}", _byDisplayId.Count);
    }

    /// <summary>URL иконки для displayid (PNG в wwwroot/icons); заглушка, если соответствия нет.</summary>
    public string IconUrl(uint displayId) =>
        $"/icons/{(_byDisplayId.TryGetValue(displayId, out var name) ? name : FallbackIcon)}.png";
}
