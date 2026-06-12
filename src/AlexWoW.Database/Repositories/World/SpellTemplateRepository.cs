using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>Спеллы БД мира (<c>spell_template</c>, дамп Spell.dbc). SRP-репозиторий (M10.2).</summary>
public sealed class SpellTemplateRepository(string connectionString)
    : MangosRepositoryBase(connectionString), ISpellTemplateRepository
{
    private const string SpellColumns = """
        SELECT Id, Attributes, SchoolMask, CastingTimeIndex, PowerType, ManaCost, ManaCostPercentage,
               RecoveryTime, CategoryRecoveryTime, StartRecoveryTime, DurationIndex,
               Effect1, Effect2, Effect3,
               EffectBasePoints1, EffectBasePoints2, EffectBasePoints3,
               EffectDieSides1, EffectDieSides2, EffectDieSides3,
               EffectApplyAuraName1, EffectApplyAuraName2, EffectApplyAuraName3,
               EffectAmplitude1, EffectAmplitude2, EffectAmplitude3,
               EffectTriggerSpell1, EffectTriggerSpell2, EffectTriggerSpell3,
               EffectMiscValue1, EffectMiscValue2, EffectMiscValue3,
               SpellFamilyName, SpellFamilyFlags, SpellFamilyFlags2,
               EffectSpellClassMask1_1, EffectSpellClassMask1_2, EffectSpellClassMask1_3,
               EffectSpellClassMask2_1, EffectSpellClassMask2_2, EffectSpellClassMask2_3,
               EffectSpellClassMask3_1, EffectSpellClassMask3_2, EffectSpellClassMask3_3,
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
        // spell_chain.prev_spell — предыдущий ранг (0, если ранг 1). M10.3.
        var prev = await db.ExecuteScalarAsync<uint?>(new CommandDefinition(
            "SELECT prev_spell FROM spell_chain WHERE spell_id = @spellId;",
            new { spellId }, cancellationToken: ct)) ?? 0u;
        if (prev != 0)
            return prev;

        // Фолбэк: в дампе spell_chain неполна (нет, напр., рангов аур/печатей паладина — 64 из 175). Тогда
        // предыдущий ранг ищем в spell_template: тот же SpellName, реальный ранг (Rank1<>'') и ближайший
        // меньший SpellLevel. Нужно для SUPERCEDED при изучении (иначе на панели/в книге копятся все ранги). M7 #47.
        return await db.ExecuteScalarAsync<uint?>(new CommandDefinition("""
            SELECT s2.Id
            FROM spell_template s1
            JOIN spell_template s2
              ON s2.SpellName = s1.SpellName AND s2.Rank1 <> '' AND s2.SpellLevel < s1.SpellLevel
            WHERE s1.Id = @spellId AND s1.Rank1 <> ''
            ORDER BY s2.SpellLevel DESC
            LIMIT 1;
            """, new { spellId }, cancellationToken: ct)) ?? 0u;
    }

    public async Task<IReadOnlyDictionary<uint, uint>> GetPrevRanksAsync(
        IReadOnlyCollection<uint> spellIds, CancellationToken ct = default)
    {
        if (spellIds.Count == 0)
            return new Dictionary<uint, uint>();
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<(uint SpellId, uint PrevSpell)>(new CommandDefinition(
            "SELECT spell_id, prev_spell FROM spell_chain WHERE spell_id IN @spellIds AND prev_spell <> 0;",
            new { spellIds }, cancellationToken: ct));
        return rows.ToDictionary(r => r.SpellId, r => r.PrevSpell);
    }

}
