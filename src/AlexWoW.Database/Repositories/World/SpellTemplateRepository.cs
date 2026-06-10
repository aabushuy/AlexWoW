using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>Спеллы БД мира (<c>spell_template</c>, дамп Spell.dbc). SRP-репозиторий (M10.2).</summary>
public sealed class SpellTemplateRepository(string connectionString)
    : MangosRepositoryBase(connectionString), ISpellTemplateRepository
{
    private const string SpellColumns = """
        SELECT Id, SchoolMask, CastingTimeIndex, PowerType, ManaCost, ManaCostPercentage,
               RecoveryTime, CategoryRecoveryTime, StartRecoveryTime, DurationIndex,
               Effect1, Effect2, Effect3,
               EffectBasePoints1, EffectBasePoints2, EffectBasePoints3,
               EffectDieSides1, EffectDieSides2, EffectDieSides3,
               EffectApplyAuraName1, EffectApplyAuraName2, EffectApplyAuraName3,
               EffectAmplitude1, EffectAmplitude2, EffectAmplitude3,
               EffectTriggerSpell1, EffectTriggerSpell2, EffectTriggerSpell3,
               EffectMiscValue1, EffectMiscValue2, EffectMiscValue3,
               EffectItemType1, EffectItemType2, EffectItemType3,
               Reagent1, Reagent2, Reagent3, Reagent4, Reagent5, Reagent6, Reagent7, Reagent8,
               ReagentCount1, ReagentCount2, ReagentCount3, ReagentCount4,
               ReagentCount5, ReagentCount6, ReagentCount7, ReagentCount8
        FROM spell_template
        """;

    public async Task<SpellTemplateData?> GetSpellAsync(uint id, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<SpellTemplateData>(new CommandDefinition(
            SpellColumns + " WHERE Id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SpellTemplateData>> GetSpellsAsync(IReadOnlyCollection<uint> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return [];
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<SpellTemplateData>(new CommandDefinition(
            SpellColumns + " WHERE Id IN @ids;", new { ids }, cancellationToken: ct));
        return [.. rows];
    }

    public async Task<uint> GetPrevRankAsync(uint spellId, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        // spell_chain.prev_spell — предыдущий ранг (0, если ранг 1 или спелл не в цепочке). M10.3.
        return await db.ExecuteScalarAsync<uint?>(new CommandDefinition(
            "SELECT prev_spell FROM spell_chain WHERE spell_id = @spellId;",
            new { spellId }, cancellationToken: ct)) ?? 0u;
    }
}
