using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>Спеллы БД мира (<c>spell_template</c>, дамп Spell.dbc). SRP-репозиторий (M10.2).</summary>
public sealed class SpellTemplateRepository(string connectionString)
    : MangosRepositoryBase(connectionString), ISpellTemplateRepository
{
    public async Task<SpellTemplateData?> GetSpellAsync(uint id, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<SpellTemplateData>(new CommandDefinition("""
            SELECT Id, SchoolMask, CastingTimeIndex, PowerType, ManaCost, ManaCostPercentage,
                   RecoveryTime, CategoryRecoveryTime, StartRecoveryTime,
                   Effect1, Effect2, Effect3,
                   EffectBasePoints1, EffectBasePoints2, EffectBasePoints3,
                   EffectDieSides1, EffectDieSides2, EffectDieSides3
            FROM spell_template WHERE Id = @id;
            """, new { id }, cancellationToken: ct));
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
