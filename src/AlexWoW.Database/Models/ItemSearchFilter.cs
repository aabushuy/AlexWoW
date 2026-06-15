using Dapper;

namespace AlexWoW.Database.Models;

/// <summary>Класс предмета для фильтра поиска: оружие (item_template.class=2) или доспех (class=4).</summary>
public enum ItemKind
{
    Weapon = 2,
    Armor = 4,
}

/// <summary>
/// Фильтр поиска предметов в админке по <c>item_template</c>. Все поля необязательны: пустое поле
/// — без ограничения. <see cref="BuildWhere"/> собирает параметризованный WHERE (выделено отдельно,
/// чтобы покрывать тестами без БД).
/// </summary>
public sealed record ItemSearchFilter
{
    /// <summary>Требуемый уровень персонажа — нижняя граница (RequiredLevel ≥).</summary>
    public uint? LevelMin { get; init; }

    /// <summary>Требуемый уровень персонажа — верхняя граница (RequiredLevel ≤).</summary>
    public uint? LevelMax { get; init; }

    /// <summary>Класс персонажа (WotLK id): показываем вещи, подходящие этому классу. null — любой.</summary>
    public byte? PlayerClass { get; init; }

    /// <summary>Тип предмета (оружие/доспех). null — любой.</summary>
    public ItemKind? Kind { get; init; }

    /// <summary>Подстрока названия. Пробелы трактуются как «%» (как в ручном LIKE '%a%b%').</summary>
    public string? NameContains { get; init; }

    /// <summary>Максимум строк в выдаче (как LIMIT 100 в ручном запросе).</summary>
    public int Limit { get; init; } = 100;

    /// <summary>
    /// Собирает условие WHERE (без слова WHERE) и заполняет Dapper-параметры. Пустой фильтр → "1=1".
    /// </summary>
    public string BuildWhere(DynamicParameters parameters)
    {
        var clauses = new List<string>();

        if (LevelMin is { } min)
        {
            clauses.Add("RequiredLevel >= @lvlMin");
            parameters.Add("lvlMin", min);
        }
        if (LevelMax is { } max)
        {
            clauses.Add("RequiredLevel <= @lvlMax");
            parameters.Add("lvlMax", max);
        }
        if (Kind is { } kind)
        {
            clauses.Add("class = @class");
            parameters.Add("class", (uint)kind);
        }
        if (PlayerClass is { } cls && cls is >= 1 and <= 11)
        {
            // AllowableClass: -1 — для всех классов; иначе битмаска (бит = 1 << (classId-1)).
            clauses.Add("(AllowableClass = -1 OR (AllowableClass & @classMask) <> 0)");
            parameters.Add("classMask", 1 << (cls - 1));
        }
        if (!string.IsNullOrWhiteSpace(NameContains))
        {
            clauses.Add("name LIKE @name");
            // «Gladiator Plate» → «%Gladiator%Plate%» (как ручной поиск пользователя).
            var pattern = "%" + string.Join('%',
                NameContains.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) + "%";
            parameters.Add("name", pattern);
        }

        return clauses.Count == 0 ? "1=1" : string.Join(" AND ", clauses);
    }
}
