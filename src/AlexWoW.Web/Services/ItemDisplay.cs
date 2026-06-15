using AlexWoW.Database.Models;

namespace AlexWoW.Web.Services;

/// <summary>
/// Справочники предметов для UI админки (WotLK 3.3.5a): цвет/название качества, слот (InventoryType),
/// тип (class+subclass), названия характеристик, список допустимых классов. Только для отображения —
/// игровую логику не трогает (как <see cref="GameData"/>).
/// </summary>
public static class ItemDisplay
{
    // Цвета качества (ItemQuality 0..7) — как в клиенте.
    private static readonly string[] QualityColors =
    [
        "#9d9d9d", // 0 Низкое
        "#ffffff", // 1 Обычное
        "#1eff00", // 2 Необычное
        "#0070dd", // 3 Редкое
        "#a335ee", // 4 Эпическое
        "#ff8000", // 5 Легендарное
        "#e6cc80", // 6 Артефакт
        "#e6cc80", // 7 Наследие
    ];

    private static readonly string[] QualityNames =
    [
        "Низкое", "Обычное", "Необычное", "Редкое", "Эпическое", "Легендарное", "Артефакт", "Наследие",
    ];

    // Название слота по InventoryType (0..28).
    private static readonly Dictionary<uint, string> InvTypes = new()
    {
        [1] = "Голова", [2] = "Шея", [3] = "Плечо", [4] = "Рубаха", [5] = "Грудь", [6] = "Пояс",
        [7] = "Ноги", [8] = "Ступни", [9] = "Запястья", [10] = "Кисти рук", [11] = "Палец",
        [12] = "Аксессуар", [13] = "Одноручное", [14] = "Левая рука", [15] = "Дальний бой",
        [16] = "Спина", [17] = "Двуручное", [18] = "Сумка", [19] = "Гербовая накидка",
        [20] = "Грудь", [21] = "Правая рука", [22] = "Левая рука", [23] = "Левая рука",
        [24] = "Боеприпасы", [25] = "Метательное", [26] = "Дальний бой", [27] = "Колчан",
        [28] = "Реликвия",
    };

    // Подклассы оружия (item_template.class = 2).
    private static readonly Dictionary<uint, string> WeaponSub = new()
    {
        [0] = "Топор (одноруч.)", [1] = "Топор (двуруч.)", [2] = "Лук", [3] = "Ружьё",
        [4] = "Палица (одноруч.)", [5] = "Палица (двуруч.)", [6] = "Древковое",
        [7] = "Меч (одноруч.)", [8] = "Меч (двуруч.)", [10] = "Посох", [13] = "Кистевое",
        [14] = "Разное", [15] = "Кинжал", [16] = "Метательное", [18] = "Арбалет",
        [19] = "Жезл", [20] = "Удочка",
    };

    // Подклассы брони (item_template.class = 4).
    private static readonly Dictionary<uint, string> ArmorSub = new()
    {
        [0] = "Разное", [1] = "Ткань", [2] = "Кожа", [3] = "Кольчуга", [4] = "Латы",
        [6] = "Щит", [7] = "Манускрипт", [8] = "Идол", [9] = "Тотем", [10] = "Печать",
    };

    // Названия характеристик (ItemModType) для тултипа.
    private static readonly Dictionary<uint, string> StatNames = new()
    {
        [0] = "к мане", [1] = "к здоровью", [3] = "к ловкости", [4] = "к силе",
        [5] = "к интеллекту", [6] = "к духу", [7] = "к выносливости",
        [12] = "к рейтингу защиты", [13] = "к рейтингу уклонения", [14] = "к рейтингу парирования",
        [15] = "к рейтингу блока", [16] = "к рейтингу меткости (дальн.)", [17] = "к рейтингу меткости (ближ.)",
        [18] = "к рейтингу меткости (закл.)", [19] = "к рейтингу крита (дальн.)", [20] = "к рейтингу крита (ближ.)",
        [21] = "к рейтингу крита (закл.)", [28] = "к рейтингу скорости (дальн.)", [29] = "к рейтингу скорости (ближ.)",
        [30] = "к рейтингу скорости (закл.)", [31] = "к рейтингу меткости", [32] = "к рейтингу крит. удара",
        [35] = "к устойчивости", [36] = "к рейтингу скорости", [37] = "к мастерству",
        [38] = "к силе атаки", [39] = "к силе атаки (дальн.)", [43] = "к восст. маны",
        [44] = "к пробиванию брони", [45] = "к силе заклинаний", [46] = "к восст. здоровья",
        [47] = "к проникающей способности заклинаний", [48] = "к величине блока",
    };

    public static string QualityColor(uint quality) =>
        quality < QualityColors.Length ? QualityColors[quality] : QualityColors[1];

    public static string QualityName(uint quality) =>
        quality < QualityNames.Length ? QualityNames[quality] : $"Качество #{quality}";

    /// <summary>Слот предмета по InventoryType, либо null если предмет не экипируется.</summary>
    public static string? SlotName(uint inventoryType) =>
        InvTypes.TryGetValue(inventoryType, out var name) ? name : null;

    /// <summary>Человекочитаемый тип «класс — подкласс» (Оружие — Меч, Доспех — Ткань, …).</summary>
    public static string TypeName(uint itemClass, uint subClass) => itemClass switch
    {
        0 => "Расходник",
        1 => "Контейнер",
        2 => "Оружие — " + WeaponSub.GetValueOrDefault(subClass, "разное"),
        3 => "Самоцвет",
        4 => "Доспех — " + ArmorSub.GetValueOrDefault(subClass, "разное"),
        5 => "Реагент",
        6 => "Снаряд",
        7 => "Хозтовары",
        9 => "Рецепт",
        11 => "Колчан",
        12 => "Задание",
        13 => "Ключ",
        15 => "Разное",
        16 => "Глиф",
        _ => $"Класс #{itemClass}",
    };

    /// <summary>Название характеристики (для строк «+N к …» в тултипе).</summary>
    public static string StatName(uint statType) =>
        StatNames.GetValueOrDefault(statType, $"к хар-ке #{statType}");

    /// <summary>
    /// Список классов, которым подходит предмет (AllowableClass-битмаска), либо null — для всех.
    /// </summary>
    public static IReadOnlyList<string>? AllowableClasses(int allowableClass)
    {
        if (allowableClass == -1 || allowableClass == 0)
            return null; // -1/0 — без ограничения по классу
        var names = new List<string>();
        for (byte cls = 1; cls <= 11; cls++)
            if ((allowableClass & (1 << (cls - 1))) != 0)
                names.Add(GameData.ClassName(cls));
        return names.Count == 0 ? null : names;
    }

    /// <summary>Скорость оружия в секундах (delay в мс).</summary>
    public static float SpeedSeconds(uint delay) => delay / 1000f;

    /// <summary>DPS оружия по урону и задержке (0 если нет урона/задержки).</summary>
    public static float Dps(ItemTemplateData item)
    {
        var delay = item.Delay;
        if (delay == 0)
            return 0f;
        var min = 0f;
        var max = 0f;
        foreach (var d in item.Damages)
        {
            min += d.Min;
            max += d.Max;
        }
        return (min + max) / 2f / (delay / 1000f);
    }
}
