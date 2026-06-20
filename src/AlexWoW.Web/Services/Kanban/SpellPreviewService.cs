using AlexWoW.Database.Util;
using AlexWoW.Web;
using Dapper;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace AlexWoW.Web.Services.Kanban;

/// <summary>
/// Подгрузка минимального тултипа спелла для регрессионных тикетов канбана (Phase E плана).
/// Источник — <c>mangos.spell_template</c>. Только web-просмотр, не пересекается с серверным
/// <see cref="AlexWoW.Database.Abstractions.ISpellTemplateRepository"/> (там 100+ полей под движок каста).
/// Здесь нужны только поля, видимые в игровом тултипе.
/// </summary>
public sealed class SpellPreviewService(IOptions<WebOptions> options, SpellIconService icons)
{
    private readonly string _cs = options.Value.WorldConnectionString;

    public bool Configured => !string.IsNullOrWhiteSpace(_cs);

    public async Task<SpellPreview?> GetAsync(uint spellId, CancellationToken ct)
    {
        if (!Configured) return null;
        await using var c = new MySqlConnection(_cs);
        await c.OpenAsync(ct);
        var row = await c.QuerySingleOrDefaultAsync<SpellRow>(new CommandDefinition(
            "SELECT Id, SpellName, SpellLevel, SpellFamilyName, SchoolMask, ManaCost, PowerType, " +
            "       Effect1, EffectApplyAuraName1, EffectBasePoints1, EffectDieSides1, " +
            "       Effect2, EffectBasePoints2, Effect3, EffectBasePoints3, " +
            "       RecoveryTime, DurationIndex, SpellIconID " +
            "FROM mangos.spell_template WHERE Id = @id",
            new { id = spellId }, cancellationToken: ct));

        return row is null ? null : new SpellPreview(
            Id: row.Id,
            Name: row.SpellName,
            Level: row.SpellLevel,
            FamilyName: SpellMeta.FamilyName(row.SpellFamilyName),
            School: SpellMeta.SchoolName(row.SchoolMask),
            ManaCost: row.ManaCost,
            PowerType: SpellMeta.PowerType(row.PowerType),
            Effect1: SpellMeta.EffectName(row.Effect1),
            EffectAura1: row.EffectApplyAuraName1 == 0 ? null : SpellMeta.AuraName(row.EffectApplyAuraName1),
            BasePoints1: row.EffectBasePoints1 + 1, // CMaNGOS-конвенция: BasePoints хранится со смещением -1
            DieSides1: row.EffectDieSides1,
            Effect2: row.Effect2 == 0 ? null : SpellMeta.EffectName(row.Effect2),
            Effect3: row.Effect3 == 0 ? null : SpellMeta.EffectName(row.Effect3),
            RecoveryMs: row.RecoveryTime,
            DurationIndex: row.DurationIndex,
            IconUrl: icons.IconUrl(row.SpellIconID));
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
        public uint RecoveryTime { get; init; }
        public uint DurationIndex { get; init; }
        public uint SpellIconID { get; init; }
    }
}

/// <summary>Срез spell_template для preview-блока на /Ticket.</summary>
public sealed record SpellPreview(
    uint Id,
    string Name,
    uint Level,
    string FamilyName,
    string School,
    uint ManaCost,
    string PowerType,
    string Effect1,
    string? EffectAura1,
    int BasePoints1,
    int DieSides1,
    string? Effect2,
    string? Effect3,
    uint RecoveryMs,
    uint DurationIndex,
    string IconUrl);
