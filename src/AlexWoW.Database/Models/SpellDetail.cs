namespace AlexWoW.Database.Models;

/// <summary>Один реагент рецепта: предмет + количество + имя (для таблицы реагентов в аддоне).</summary>
public sealed record SpellReagent(uint ItemId, uint Count, string Name);

/// <summary>
/// Срез <c>mangos.spell_template</c> для блока «детали спелла» в аддоне AlexQATester (вкладки
/// Абилки/Таланты/Профессии). Описание спелла берёт сам клиент (тултип spell.dbc); здесь — то, чего в
/// клиентском тултипе нет: школа/семейство/эффекты + реагенты рецепта. <see cref="Effects"/> уже
/// сформированы в строки (через <see cref="Util.SpellMeta"/>). Не пересекается с движковым
/// <c>ISpellTemplateRepository</c>.
/// </summary>
public sealed record SpellDetail(
    uint Id,
    string Name,
    string School,
    string Family,
    uint Level,
    uint ManaCost,
    string PowerType,
    IReadOnlyList<string> Effects,
    IReadOnlyList<SpellReagent> Reagents);
