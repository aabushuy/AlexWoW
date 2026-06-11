using AlexWoW.Database.Models;

namespace AlexWoW.Database.Analysis;

/// <summary>Вид аномалии заклинания, найденной анализом захвата (M12 Spell QA).</summary>
public enum SpellAnomalyKind : byte
{
    /// <summary>Урон/хил ниже эталонного минимума (не weapon-абилка).</summary>
    BelowExpected,

    /// <summary>Урон/хил выше эталонного максимума (не weapon-абилка).</summary>
    AboveExpected,

    /// <summary>Прямой урон с нулевой вычисленной величиной (спелл «впустую»).</summary>
    ZeroDamage,

    /// <summary>Прямой хил с нулевой вычисленной величиной (нет лечащего эффекта).</summary>
    ZeroHeal,

    /// <summary>Урон без школы магии (school=0) — вероятно, не разобран SchoolMask.</summary>
    MissingSchool,
}

/// <summary>Одна аномалия по спеллу (агрегирована по спеллу+виду, не на каждый каст).</summary>
public sealed record SpellAnomaly(uint SpellId, SpellTestResultType ResultType, SpellAnomalyKind Kind, string Detail);

/// <summary>Итог анализа сессии захвата: счётчики + список аномалий.</summary>
public sealed record SpellTestAnalysis(int TotalResults, int DistinctSpells, IReadOnlyList<SpellAnomaly> Anomalies)
{
    public bool HasAnomalies => Anomalies.Count > 0;
}

/// <summary>
/// Анализ захваченных результатов проверки заклинаний (M12 Spell QA, чистая логика): сверяет вычисленные
/// сервером значения с эталоном (<c>expected_min/max</c>, школа), сохранённым в строке в момент захвата.
/// Эталон взят из <c>spell_template</c> на world-сервере, поэтому Web анализирует без доступа к mangos.
/// </summary>
public static class SpellTestAnalyzer
{
    /// <summary>Аномалия урона/хила вне эталонного диапазона учитывается только для не-weapon-абилок:
    /// у weapon-абилок величина включает бросок урона оружия и закономерно выходит за [min;max].</summary>
    public static SpellTestAnalysis Analyze(IReadOnlyList<SpellTestResult> results)
    {
        var anomalies = new List<SpellAnomaly>();
        foreach (var r in results)
        {
            var isTick = r.ResultType is SpellTestResultType.DotTick or SpellTestResultType.HotTick;

            // Вне эталонного диапазона (не weapon, есть верхняя граница).
            if (!r.WeaponBased && r.ExpectedMax > 0)
            {
                if (r.Amount < r.ExpectedMin)
                    anomalies.Add(new(r.SpellId, r.ResultType, SpellAnomalyKind.BelowExpected,
                        $"величина {r.Amount} < ожидаемого минимума {r.ExpectedMin}"));
                else if (r.Amount > r.ExpectedMax)
                    anomalies.Add(new(r.SpellId, r.ResultType, SpellAnomalyKind.AboveExpected,
                        $"величина {r.Amount} > ожидаемого максимума {r.ExpectedMax}"));
            }

            // Нулевая вычисленная величина прямого эффекта (effective==0 — норма: цель полна/мертва, это не баг спелла).
            if (r.ResultType == SpellTestResultType.DirectDamage && r.Amount == 0)
                anomalies.Add(new(r.SpellId, r.ResultType, SpellAnomalyKind.ZeroDamage, "нулевой вычисленный урон"));
            if (r.ResultType == SpellTestResultType.DirectHeal && r.Amount == 0)
                anomalies.Add(new(r.SpellId, r.ResultType, SpellAnomalyKind.ZeroHeal, "нулевой вычисленный хил"));

            // Урон без школы магии.
            if (!r.IsHeal && !isTick && r.Amount > 0 && r.School == 0)
                anomalies.Add(new(r.SpellId, r.ResultType, SpellAnomalyKind.MissingSchool, "урон без школы магии (school=0)"));
        }

        var distinct = results.Select(r => r.SpellId).Distinct().Count();
        // Дедуп: одна аномалия на (спелл, вид) — не плодим одинаковые на каждый из N кастов.
        var deduped = anomalies
            .GroupBy(a => (a.SpellId, a.Kind))
            .Select(g => g.First())
            .OrderBy(a => a.SpellId).ThenBy(a => (int)a.Kind)
            .ToList();
        return new SpellTestAnalysis(results.Count, distinct, deduped);
    }
}
