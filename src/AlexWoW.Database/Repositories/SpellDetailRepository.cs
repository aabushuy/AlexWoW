using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.Database.Util;
using Dapper;
using MySqlConnector;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// Dapper-доступ к деталям спелла для аддона AlexQATester: <c>mangos.spell_template</c> (школа/семейство/
/// эффекты) + имена реагентов из <c>mangos.item_template</c>. Поля и маппинги синхронны web-просмотру
/// (<c>SpellPreviewService</c>) через общий <see cref="SpellMeta"/>. Read-only.
/// </summary>
public sealed class SpellDetailRepository(string connectionString) : ISpellDetailRepository
{
    private readonly string _cs = connectionString;

    public bool Configured => !string.IsNullOrWhiteSpace(_cs);

    public async Task<SpellDetail?> GetAsync(uint spellId, CancellationToken ct = default)
    {
        if (!Configured) return null;
        await using var c = new MySqlConnection(_cs);
        await c.OpenAsync(ct);

        var row = await c.QuerySingleOrDefaultAsync<SpellRow>(new CommandDefinition(
            "SELECT Id, SpellName, SpellLevel, SpellFamilyName, SchoolMask, ManaCost, PowerType, " +
            "       Effect1, EffectApplyAuraName1, EffectBasePoints1, EffectDieSides1, " +
            "       Effect2, EffectBasePoints2, Effect3, EffectBasePoints3, " +
            "       Reagent1, ReagentCount1, Reagent2, ReagentCount2, Reagent3, ReagentCount3, " +
            "       Reagent4, ReagentCount4, Reagent5, ReagentCount5, Reagent6, ReagentCount6, " +
            "       Reagent7, ReagentCount7, Reagent8, ReagentCount8 " +
            "FROM mangos.spell_template WHERE Id = @id",
            new { id = spellId }, cancellationToken: ct));
        if (row is null) return null;

        var effects = new List<string>(3);
        if (row.Effect1 != 0)
            effects.Add(EffectLine(row.Effect1, row.EffectBasePoints1, row.EffectDieSides1, row.EffectApplyAuraName1));
        if (row.Effect2 != 0)
            effects.Add(EffectLine(row.Effect2, row.EffectBasePoints2, 0, 0));
        if (row.Effect3 != 0)
            effects.Add(EffectLine(row.Effect3, row.EffectBasePoints3, 0, 0));

        var reagents = await LoadReagentsAsync(c, row, ct);

        return new SpellDetail(
            Id: row.Id,
            Name: row.SpellName,
            School: SpellMeta.SchoolName(row.SchoolMask),
            Family: SpellMeta.FamilyName(row.SpellFamilyName),
            Level: row.SpellLevel,
            ManaCost: row.ManaCost,
            PowerType: SpellMeta.PowerType(row.PowerType),
            Effects: effects,
            Reagents: reagents);
    }

    // Имя эффекта + значение (BasePoints в CMaNGOS со смещением -1) + аура, если есть.
    private static string EffectLine(int effect, int basePoints, int dieSides, int auraName)
    {
        var line = SpellMeta.EffectName(effect);
        var bp = basePoints + 1;
        string? value = dieSides > 1 ? $"{bp}-{bp + dieSides - 1}" : (bp != 0 ? bp.ToString() : null);
        if (value is not null) line += $": {value}";
        if (auraName != 0) line += $" / аура: {SpellMeta.AuraName(auraName)}";
        return line;
    }

    // Реагенты Reagent1..8/ReagentCount1..8 (item>0 && count>0) + имена из item_template (порядок сохраняем).
    private static async Task<IReadOnlyList<SpellReagent>> LoadReagentsAsync(
        MySqlConnection c, SpellRow row, CancellationToken ct)
    {
        var raw = new List<(int Item, uint Count)>(8);
        Add(raw, row.Reagent1, row.ReagentCount1); Add(raw, row.Reagent2, row.ReagentCount2);
        Add(raw, row.Reagent3, row.ReagentCount3); Add(raw, row.Reagent4, row.ReagentCount4);
        Add(raw, row.Reagent5, row.ReagentCount5); Add(raw, row.Reagent6, row.ReagentCount6);
        Add(raw, row.Reagent7, row.ReagentCount7); Add(raw, row.Reagent8, row.ReagentCount8);
        if (raw.Count == 0) return [];

        var ids = raw.Select(r => r.Item).Distinct().ToArray();
        var names = (await c.QueryAsync<(int Entry, string Name)>(new CommandDefinition(
                "SELECT entry AS Entry, name AS Name FROM mangos.item_template WHERE entry IN @ids",
                new { ids }, cancellationToken: ct)))
            .ToDictionary(r => r.Entry, r => r.Name);

        return raw.Select(r => new SpellReagent(
            (uint)r.Item, r.Count, names.TryGetValue(r.Item, out var n) ? n : $"#{r.Item}")).ToList();
    }

    private static void Add(List<(int, uint)> list, int item, uint count)
    {
        if (item > 0 && count > 0) list.Add((item, count));
    }

    private sealed record SpellRow
    {
        public uint Id { get; init; }
        public string SpellName { get; init; } = "";
        public uint SpellLevel { get; init; }
        public uint SpellFamilyName { get; init; }
        public uint SchoolMask { get; init; }
        public uint ManaCost { get; init; }
        public int PowerType { get; init; }
        public int Effect1 { get; init; }
        public int EffectApplyAuraName1 { get; init; }
        public int EffectBasePoints1 { get; init; }
        public int EffectDieSides1 { get; init; }
        public int Effect2 { get; init; }
        public int EffectBasePoints2 { get; init; }
        public int Effect3 { get; init; }
        public int EffectBasePoints3 { get; init; }
        public int Reagent1 { get; init; }
        public uint ReagentCount1 { get; init; }
        public int Reagent2 { get; init; }
        public uint ReagentCount2 { get; init; }
        public int Reagent3 { get; init; }
        public uint ReagentCount3 { get; init; }
        public int Reagent4 { get; init; }
        public uint ReagentCount4 { get; init; }
        public int Reagent5 { get; init; }
        public uint ReagentCount5 { get; init; }
        public int Reagent6 { get; init; }
        public uint ReagentCount6 { get; init; }
        public int Reagent7 { get; init; }
        public uint ReagentCount7 { get; init; }
        public int Reagent8 { get; init; }
        public uint ReagentCount8 { get; init; }
    }
}
