namespace AlexWoW.Database.Util;

/// <summary>
/// Маппинги числовых полей <c>spell_template</c> в человекочитаемые RU-строки для UI (тултипы спеллов).
/// Единый источник правды: используется и web-просмотром (<c>SpellPreviewService</c>), и серверной
/// отдачей деталей спелла в аддон (<c>AddonProtocol qaspell</c>). Только presentation-маппинг, без
/// данных движка каста (там <c>ISpellTemplateRepository</c> со 100+ полями).
/// </summary>
public static class SpellMeta
{
    /// <summary>Школа магии по <c>SchoolMask</c>.</summary>
    public static string SchoolName(uint mask) => mask switch
    {
        1 => "Физическая",
        2 => "Священная",
        4 => "Огонь",
        8 => "Природа",
        16 => "Лёд",
        32 => "Тень",
        64 => "Тайная магия",
        _ => $"маска {mask}",
    };

    /// <summary>Тип ресурса по <c>PowerType</c>.</summary>
    public static string PowerType(int p) => p switch
    {
        0 => "мана",
        1 => "ярость",
        2 => "фокус",
        3 => "энергия",
        5 => "здоровье",
        6 => "рунич. сила",
        7 => "руны",
        _ => $"power#{p}",
    };

    /// <summary>Класс/семейство спелла по <c>SpellFamilyName</c>.</summary>
    public static string FamilyName(uint f) => f switch
    {
        0 => "Общие/расовые",
        3 => "Маг",
        4 => "Воин",
        5 => "Чернокнижник",
        6 => "Жрец",
        7 => "Друид",
        8 => "Разбойник",
        9 => "Охотник",
        10 => "Паладин",
        11 => "Шаман",
        15 => "Рыцарь смерти",
        _ => $"family#{f}",
    };

    // Самые частые SpellEffect (см. tools/regression-import/template.py — синхронно).
    public static string EffectName(int eff) => eff switch
    {
        0 => "—",
        2 => "Прямой урон школой (SCHOOL_DAMAGE)",
        3 => "Dummy (скриптовый)",
        6 => "Наложение ауры (APPLY_AURA)",
        8 => "Восстановление ресурса (ENERGIZE)",
        10 => "Прямое лечение (HEAL)",
        24 => "Создать предмет",
        26 => "Открыть замок",
        38 => "Прерывание каста",
        64 => "Триггер другого спелла",
        77 => "Скриптовый эффект",
        108 => "Применить глиф",
        113 => "Proficiency",
        _ => $"effect#{eff}",
    };

    public static string AuraName(int aura) => aura switch
    {
        3 => "Dummy",
        4 => "Конфьюз",
        7 => "Страх",
        8 => "Периодическое лечение (HoT)",
        12 => "Стан",
        13 => "+ урон",
        15 => "Damage shield",
        22 => "+ сопротивление",
        23 => "Триггер периодически",
        24 => "Периодическое восстановление",
        27 => "Periodic leech",
        29 => "+ статы",
        31 => "+ скорость передвижения",
        33 => "- скорость",
        50 => "Прок-триггер",
        99 => "+ ATK Power",
        107 => "Spellmod (flat)",
        108 => "Spellmod (pct)",
        158 => "+ хил",
        216 => "+ скорость каста",
        _ => $"aura#{aura}",
    };
}
