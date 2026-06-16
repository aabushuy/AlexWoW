using Dapper;

namespace AlexWoW.Database.Models;

/// <summary>Класс предмета для фильтра поиска (item_template.class): расходник (0), оружие (2), доспех (4).</summary>
public enum ItemKind
{
    Consumable = 0,
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

    /// <summary>Тип предмета (расходник/оружие/доспех). null — любой. Игнорируется, если задан <see cref="Classes"/>.</summary>
    public ItemKind? Kind { get; init; }

    /// <summary>Набор классов предмета (item_template.class) для <c>class IN (...)</c> — для «экипировка»
    /// (2,4) и т.п. Имеет приоритет над <see cref="Kind"/>. null/пусто — без ограничения по классу.</summary>
    public IReadOnlyCollection<uint>? Classes { get; init; }

    /// <summary>Минимальное качество (item_template.Quality ≥). null — любое. 0 серый … 4 эпик …</summary>
    public uint? QualityMin { get; init; }

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
        if (Classes is { Count: > 0 })
        {
            clauses.Add("class IN @classes");
            parameters.Add("classes", Classes);
        }
        else if (Kind is { } kind)
        {
            clauses.Add("class = @class");
            parameters.Add("class", (uint)kind);
        }
        if (QualityMin is { } qmin)
        {
            clauses.Add("Quality >= @qmin");
            parameters.Add("qmin", qmin);
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
