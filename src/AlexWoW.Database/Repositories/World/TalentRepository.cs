using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>Таланты БД мира (talent ⨝ talent_tab). SRP-репозиторий (M9.7), Dapper read-only + кэш
/// (≈900 записей — грузим все разом, валидация изучения идёт в памяти).</summary>
public sealed class TalentRepository(string connectionString)
    : MangosRepositoryBase(connectionString), ITalentRepository
{
    private IReadOnlyDictionary<uint, TalentData>? _cache;

    public async Task<IReadOnlyDictionary<uint, TalentData>> GetAllTalentsAsync(CancellationToken ct = default)
    {
        if (_cache is not null)
            return _cache;

        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<TalentData>(new CommandDefinition("""
            SELECT t.TalentID AS TalentId, t.TalentTab AS TalentTab, t.Tier AS Tier,
                   tt.ClassMask AS ClassMask, t.DependsOn AS DependsOn, t.DependsOnRank AS DependsOnRank,
                   t.RankID1 AS Rank1, t.RankID2 AS Rank2, t.RankID3 AS Rank3, t.RankID4 AS Rank4, t.RankID5 AS Rank5
            FROM talent t JOIN talent_tab tt ON tt.TalentTabID = t.TalentTab;
            """, cancellationToken: ct));

        var map = new Dictionary<uint, TalentData>();
        foreach (var row in rows)
            map[row.TalentId] = row;
        return _cache = map;
    }
}
