using System.Globalization;

namespace AlexWoW.Web.Services;

/// <summary>
/// Карта SpellIconID → имя иконки спелла (офлайн из клиента 3.3.5a:
/// SpellIcon.dbc + BLP→PNG, см. <c>tools/MapExtractor spell-iconmap</c>).
/// PNG лежат в общей <c>wwwroot/icons/</c> (та же, что у предметов — много иконок переиспользуется),
/// карта — <c>wwwroot/icons/_spell-map.tsv</c>. Если карты нет — <see cref="IconUrl"/> отдаёт
/// заглушку, preview-блок отрисуется без иконки.
/// </summary>
public sealed class SpellIconService
{
    public const string FallbackIcon = "inv_misc_questionmark";

    private readonly Dictionary<uint, string> _byIconId = [];

    public SpellIconService(IWebHostEnvironment env, ILogger<SpellIconService> log)
    {
        var path = Path.Combine(env.WebRootPath ?? "wwwroot", "icons", "_spell-map.tsv");
        if (!File.Exists(path))
        {
            log.LogInformation("Карта иконок спеллов не найдена: {Path} — будут заглушки (это нормально до офлайн-экстракции)", path);
            return;
        }
        foreach (var line in File.ReadLines(path))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            if (uint.TryParse(line.AsSpan(0, tab), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                _byIconId[id] = line[(tab + 1)..].Trim();
        }
        log.LogInformation("Загружено иконок спеллов: {Count}", _byIconId.Count);
    }

    /// <summary>URL иконки для SpellIconID (PNG в общей wwwroot/icons); заглушка, если соответствия нет.</summary>
    public string IconUrl(uint iconId) =>
        $"/icons/{(_byIconId.TryGetValue(iconId, out var name) ? name : FallbackIcon)}.png";
}
