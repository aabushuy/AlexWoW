using System.Text.RegularExpressions;

namespace AlexWoW.Database.Util;

/// <summary>
/// Разбор заголовка регрессионного канбан-тикета формата «#<spellId> · …» (генератор в
/// <c>tools/regression-import/template.py</c> формирует title именно так). Один источник для Web
/// (<c>SpellPreviewService</c> на <c>/Ticket</c>) и WorldServer (<c>KanbanBoardRepository</c>, KB14).
/// </summary>
public static partial class KanbanTitleParser
{
    [GeneratedRegex(@"^#(\d+)\s*·")]
    private static partial Regex SpellIdRegex();

    /// <summary>Вернуть spell_id из заголовка или <see langword="null"/> если формат не совпал.</summary>
    public static int? TryParseSpellId(string? title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        var m = SpellIdRegex().Match(title);
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }
}
