using System.Globalization;
using System.Text;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Analysis;
using AlexWoW.Database.Models;
using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages.Admin;

/// <summary>Детали сессии захвата + анализ аномалий + готовое тело тикета (M12 Spell QA, только для админов).</summary>
public sealed class SessionModel(ISpellTestRepository spellTest) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    public SpellTestSession? Session { get; private set; }
    public SpellTestAnalysis? Analysis { get; private set; }
    public IReadOnlyList<SpellRow> Spells { get; private set; } = [];

    /// <summary>Готовое тело тикета (для копирования в внешний трекер).</summary>
    public string TicketTitle { get; private set; } = "";
    public string TicketBody { get; private set; } = "";

    /// <summary>Сводка по одному спеллу+типу эффекта: диапазон вычисленных величин против эталона.</summary>
    public sealed record SpellRow(uint SpellId, SpellTestResultType Type, byte School, int Casts,
        uint Min, uint Max, long Avg, uint ExpMin, uint ExpMax, bool WeaponBased, uint ExpectedCost);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct))
            return NotFound();
        return Page();
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Session = await spellTest.GetSessionAsync(Id, ct);
        if (Session is null)
            return false;
        var results = await spellTest.GetResultsAsync(Id, ct);
        Analysis = SpellTestAnalyzer.Analyze(results);
        Spells = [.. results
            .GroupBy(r => (r.SpellId, r.ResultType))
            .Select(g => new SpellRow(
                g.Key.SpellId, g.Key.ResultType, g.First().School, g.Count(),
                g.Min(x => x.Amount), g.Max(x => x.Amount), (long)g.Average(x => x.Amount),
                g.First().ExpectedMin, g.First().ExpectedMax, g.First().WeaponBased, g.First().ExpectedCost))
            .OrderBy(x => x.SpellId).ThenBy(x => (int)x.Type)];
        (TicketTitle, TicketBody) = BuildTicket(Session, Analysis);
        return true;
    }

    /// <summary>Формирует заголовок и markdown-тело тикета из отчёта анализатора (для следующего агента).</summary>
    private static (string Title, string Body) BuildTicket(SpellTestSession s, SpellTestAnalysis a)
    {
        var cls = GameData.ClassName(s.Class);
        var title = $"[Spell QA] {cls} ур.{s.Level} — аномалий: {a.Anomalies.Count} (сессия #{s.Id})";
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"Сессия захвата проверки заклинаний **#{s.Id}**.\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"- Класс: {cls} (id {s.Class}), уровень {s.Level}\n");
        sb.Append(CultureInfo.InvariantCulture, $"- Режим: {(s.Mode == SpellTestMode.Harness ? "авто-харнесс" : "ручной")}\n");
        sb.Append(CultureInfo.InvariantCulture, $"- Записей: {a.TotalResults}, спеллов: {a.DistinctSpells}\n");
        if (s.TalentsSlotted)
            sb.Append("- ⚠ На старте были активны таланты-модификаторы (не базовая конфигурация)\n");
        sb.Append(CultureInfo.InvariantCulture, $"\n## Аномалии ({a.Anomalies.Count})\n\n");
        foreach (var an in a.Anomalies)
            sb.Append(CultureInfo.InvariantCulture,
                $"- spell {an.SpellId} [{TypeName(an.ResultType)}] — {AnomalyName(an.Kind)}: {an.Detail}\n");
        sb.Append("\nЭталон — `spell_template` (дамп Spell.dbc). Таланты при тесте не расставлены.\n");
        return (title, sb.ToString());
    }

    /// <summary>Русское имя типа результата для UI.</summary>
    public static string TypeName(SpellTestResultType t) => t switch
    {
        SpellTestResultType.DirectDamage => "Урон",
        SpellTestResultType.DirectHeal => "Хил",
        SpellTestResultType.DotTick => "DoT",
        SpellTestResultType.HotTick => "HoT",
        _ => t.ToString(),
    };

    /// <summary>Русское имя вида аномалии для UI.</summary>
    public static string AnomalyName(SpellAnomalyKind k) => k switch
    {
        SpellAnomalyKind.BelowExpected => "Ниже эталона",
        SpellAnomalyKind.AboveExpected => "Выше эталона",
        SpellAnomalyKind.ZeroDamage => "Нулевой урон",
        SpellAnomalyKind.ZeroHeal => "Нулевой хил",
        SpellAnomalyKind.MissingSchool => "Нет школы",
        _ => k.ToString(),
    };
}
