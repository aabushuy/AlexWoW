using System.Globalization;

namespace AlexWoW.Web.Services;

/// <summary>
/// Карта SpellIconID → имя иконки спелла (планируется извлечь офлайн из клиента 3.3.5a:
/// SpellIcon.dbc + BLP→PNG, по аналогии с <see cref="ItemIconService"/> и tools/MapExtractor iconmap).
/// PNG лежат в <c>wwwroot/icons/spells/</c>, карта — <c>wwwroot/icons/spells/_map.tsv</c>.
/// Пока (Phase E плана) карта может отсутствовать — в этом случае <see cref="IconUrl"/> возвращает
/// заглушку, preview-блок отрисуется без иконки.
/// </summary>
public sealed class SpellIconService
{
    public const string FallbackIcon = "inv_misc_questionmark";

    private readonly Dictionary<uint, string> _byIconId = [];

    public SpellIconService(IWebHostEnvironment env, ILogger<SpellIconService> log)
    {
        var path = Path.Combine(env.WebRootPath ?? "wwwroot", "icons", "spells", "_map.tsv");
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

    /// <summary>URL иконки для SpellIconID (PNG в wwwroot/icons/spells); заглушка, если соответствия нет.</summary>
    public string IconUrl(uint iconId) =>
        $"/icons/spells/{(_byIconId.TryGetValue(iconId, out var name) ? name : FallbackIcon)}.png";
}
