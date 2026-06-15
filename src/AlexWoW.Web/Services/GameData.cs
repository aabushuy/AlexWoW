namespace AlexWoW.Web.Services;

/// <summary>
/// Справочники для отображения персонажей в панели (раса/класс/пол/зона/слоты/деньги).
/// Значения соответствуют WotLK 3.3.5a. Только для UI — не влияет на игровую логику.
/// </summary>
public static class GameData
{
    private static readonly Dictionary<byte, string> Races = new()
    {
        [1] = "Человек",
        [2] = "Орк",
        [3] = "Дворф",
        [4] = "Ночной эльф",
        [5] = "Нежить",
        [6] = "Таурен",
        [7] = "Гном",
        [8] = "Тролль",
        [10] = "Эльф крови",
        [11] = "Дреней",
    };

    private static readonly Dictionary<byte, string> Classes = new()
    {
        [1] = "Воин",
        [2] = "Паладин",
        [3] = "Охотник",
        [4] = "Разбойник",
        [5] = "Жрец",
        [6] = "Рыцарь смерти",
        [7] = "Шаман",
        [8] = "Маг",
        [9] = "Чернокнижник",
        [11] = "Друид",
    };

    /// <summary>Цвета классов WoW (для подсветки имени/класса).</summary>
    private static readonly Dictionary<byte, string> ClassColors = new()
    {
        [1] = "#C79C6E",
        [2] = "#F58CBA",
        [3] = "#ABD473",
        [4] = "#FFF569",
        [5] = "#FFFFFF",
        [6] = "#C41F3B",
        [7] = "#0070DE",
        [8] = "#69CCF0",
        [9] = "#9482C9",
        [11] = "#FF7D0A",
    };

    private static readonly HashSet<byte> AllianceRaces = [1, 3, 4, 7, 11];

    /// <summary>Допустимые расы по классу (WotLK 3.3.5a). Рыцарь смерти доступен всем расам.</summary>
    private static readonly Dictionary<byte, HashSet<byte>> ClassRaces = new()
    {
        [1] = [1, 3, 4, 7, 11, 2, 5, 6, 8],     // Воин
        [2] = [1, 3, 11, 10],                    // Паладин
        [3] = [3, 4, 11, 2, 6, 8, 10],           // Охотник
        [4] = [1, 3, 4, 7, 2, 5, 8, 10],         // Разбойник
        [5] = [1, 3, 4, 11, 5, 8, 10],           // Жрец
        [6] = [1, 2, 3, 4, 5, 6, 7, 8, 10, 11],  // Рыцарь смерти — все расы
        [7] = [11, 2, 6, 8],                     // Шаман
        [8] = [1, 7, 11, 5, 8, 10],              // Маг
        [9] = [1, 7, 2, 5, 10],                  // Чернокнижник
        [11] = [4, 6],                           // Друид
    };

    private static readonly Dictionary<uint, string> Maps = new()
    {
        [0] = "Восточные королевства",
        [1] = "Калимдор",
        [530] = "Запределье",
        [571] = "Нордскол",
    };

    // Небольшой набор известных зон (столицы + стартовые). Неизвестные показываем как «Зона #id».
    private static readonly Dictionary<uint, string> Zones = new()
    {
        [1] = "Дун Морог",
        [12] = "Элвиннский лес",
        [14] = "Дуротар",
        [85] = "Тирисфальские леса",
        [141] = "Тельдрассил",
        [215] = "Мулгор",
        [1497] = "Подгород",
        [1519] = "Штормград",
        [1537] = "Стальгорн",
        [1637] = "Оргриммар",
        [1638] = "Громовой Утёс",
        [1657] = "Дарнас",
        [3430] = "Аллерианский тинг",
        [3487] = "Луносвет",
        [3557] = "Экзодар",
        [3703] = "Шаттрат",
        [4395] = "Даларан",
    };

    // Названия слотов экипировки 0..18 (INVENTORY_SLOT_* 3.3.5a).
    private static readonly string[] EquipSlotNames =
    [
        "Голова", "Шея", "Плечи", "Рубаха", "Грудь", "Пояс", "Ноги", "Ступни", "Запястья",
        "Кисти рук", "Палец 1", "Палец 2", "Аксессуар 1", "Аксессуар 2", "Спина",
        "Правая рука", "Левая рука", "Дальний бой", "Гербовая накидка",
    ];

    public static string RaceName(byte race) => Races.GetValueOrDefault(race, $"Раса #{race}");

    public static string ClassName(byte cls) => Classes.GetValueOrDefault(cls, $"Класс #{cls}");

    public static string ClassColor(byte cls) => ClassColors.GetValueOrDefault(cls, "#FFD100");

    public static string GenderName(byte gender) => gender == 0 ? "Мужской" : "Женский";

    public static string Faction(byte race) => AllianceRaces.Contains(race) ? "Альянс" : "Орда";

    public static string FactionColor(byte race) => AllianceRaces.Contains(race) ? "#3E7DCC" : "#C8102E";

    public static string MapName(uint map) => Maps.GetValueOrDefault(map, $"Карта #{map}");

    public static string ZoneName(uint zone) =>
        zone == 0 ? "—" : Zones.GetValueOrDefault(zone, $"Зона #{zone}");

    /// <summary>Имя слота экипировки (0..18) либо null, если слот не экипировочный.</summary>
    public static string? EquipSlotName(byte slot) =>
        slot < EquipSlotNames.Length ? EquipSlotNames[slot] : null;

    /// <summary>Раскладывает медь на золото/серебро/медь.</summary>
    public static (uint Gold, uint Silver, uint Copper) SplitMoney(uint copper) =>
        (copper / 10000, copper / 100 % 100, copper % 100);

    /// <summary>Все игровые расы (id + имя), в каноничном порядке — для дропдаунов админ/правки.</summary>
    public static IReadOnlyList<KeyValuePair<byte, string>> AllRaces { get; } =
        [.. Races.OrderBy(r => r.Key)];

    /// <summary>Все игровые классы (id + имя), в каноничном порядке — для дропдаунов админ/фильтров.</summary>
    public static IReadOnlyList<KeyValuePair<byte, string>> AllClasses { get; } =
        [.. Classes.OrderBy(c => c.Key)];

    /// <summary>Полы (id + имя): 0 — мужской, 1 — женский.</summary>
    public static IReadOnlyList<KeyValuePair<byte, string>> AllGenders { get; } =
        [new((byte)0, GenderName(0)), new((byte)1, GenderName(1))];

    /// <summary>Существует ли такая раса (id из справочника).</summary>
    public static bool RaceExists(byte race) => Races.ContainsKey(race);

    /// <summary>Допустима ли раса для класса (WotLK 3.3.5a). Неизвестный класс — запрещаем.</summary>
    public static bool RaceAllowedForClass(byte race, byte cls) =>
        ClassRaces.TryGetValue(cls, out var races) && races.Contains(race);

    /// <summary>Расы, доступные игроку при смене (M8.6): валидные для класса И той же фракции.</summary>
    public static IReadOnlyList<KeyValuePair<byte, string>> RacesForClassSameFaction(byte cls, byte currentRace)
    {
        var alliance = AllianceRaces.Contains(currentRace);
        return [.. Races
            .Where(r => RaceAllowedForClass(r.Key, cls) && AllianceRaces.Contains(r.Key) == alliance)
            .OrderBy(r => r.Key)];
    }
}
