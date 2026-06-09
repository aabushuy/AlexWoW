using System.Globalization;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>
/// Квест-данные БД мира (quest_template + creature_questrelation/involvedrelation). SRP-репозиторий (#25).
/// </summary>
public sealed class QuestTemplateRepository(string connectionString)
    : MangosRepositoryBase(connectionString), IQuestTemplateRepository
{
    public async Task<IReadOnlyList<uint>> GetQuestGiverEntriesAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<uint>(new CommandDefinition(
            "SELECT DISTINCT id FROM creature_questrelation;", cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<uint>> GetQuestEnderEntriesAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<uint>(new CommandDefinition(
            "SELECT DISTINCT id FROM creature_involvedrelation;", cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<QuestRelation>> GetQuestGiverRelationsAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<QuestRelation>(new CommandDefinition(
            "SELECT id AS Id, quest AS Quest FROM creature_questrelation;", cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<QuestRelation>> GetQuestEnderRelationsAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<QuestRelation>(new CommandDefinition(
            "SELECT id AS Id, quest AS Quest FROM creature_involvedrelation;", cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<GiverQuest>> GetGiverQuestsAsync(uint creatureEntry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<GiverQuest>(new CommandDefinition("""
            SELECT q.entry AS QuestId, q.QuestLevel AS QuestLevel, q.QuestFlags AS QuestFlags, q.Title AS Title,
                   q.MinLevel AS MinLevel, q.RequiredRaces AS RequiredRaces, q.RequiredClasses AS RequiredClasses,
                   q.PrevQuestId AS PrevQuestId
            FROM creature_questrelation r JOIN quest_template q ON q.entry = r.quest
            WHERE r.id = @creatureEntry ORDER BY q.entry;
            """, new { creatureEntry }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<uint>> GetEnderQuestIdsAsync(uint creatureEntry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<uint>(new CommandDefinition(
            "SELECT quest FROM creature_involvedrelation WHERE id = @creatureEntry;",
            new { creatureEntry }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<QuestTemplateData?> GetQuestAsync(uint entry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var row = (IDictionary<string, object>?)await db.QueryFirstOrDefaultAsync(new CommandDefinition(
            "SELECT * FROM quest_template WHERE entry = @entry;", new { entry }, cancellationToken: ct));
        return row is null ? null : MapQuest(row);
    }

    private static QuestTemplateData MapQuest(IDictionary<string, object> r)
    {
        static uint U(IDictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v is not null ? Convert.ToUInt32(v, CultureInfo.InvariantCulture) : 0u;
        static int I(IDictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v is not null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;
        static string S(IDictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";

        return new QuestTemplateData
        {
            Entry = U(r, "entry"),
            QuestLevel = I(r, "QuestLevel"),
            MinLevel = I(r, "MinLevel"),
            ZoneOrSort = I(r, "ZoneOrSort"),
            Type = U(r, "Type"),
            Method = U(r, "Method"),
            SrcItemId = U(r, "SrcItemId"),
            NextQuestId = U(r, "NextQuestId"),
            QuestFlags = U(r, "QuestFlags"),
            SuggestedPlayers = U(r, "SuggestedPlayers"),
            Title = S(r, "Title"),
            Details = S(r, "Details"),
            Objectives = S(r, "Objectives"),
            OfferRewardText = S(r, "OfferRewardText"),
            RequestItemsText = S(r, "RequestItemsText"),
            EndText = S(r, "EndText"),
            RewOrReqMoney = I(r, "RewOrReqMoney"),
            RewXpId = U(r, "RewXPId"),
            RewSpell = U(r, "RewSpell"),
            RewSpellCast = U(r, "RewSpellCast"),
            RewItemId = [U(r, "RewItemId1"), U(r, "RewItemId2"), U(r, "RewItemId3"), U(r, "RewItemId4")],
            RewItemCount = [U(r, "RewItemCount1"), U(r, "RewItemCount2"), U(r, "RewItemCount3"), U(r, "RewItemCount4")],
            RewChoiceItemId = [U(r, "RewChoiceItemId1"), U(r, "RewChoiceItemId2"), U(r, "RewChoiceItemId3"),
                               U(r, "RewChoiceItemId4"), U(r, "RewChoiceItemId5"), U(r, "RewChoiceItemId6")],
            RewChoiceItemCount = [U(r, "RewChoiceItemCount1"), U(r, "RewChoiceItemCount2"), U(r, "RewChoiceItemCount3"),
                                  U(r, "RewChoiceItemCount4"), U(r, "RewChoiceItemCount5"), U(r, "RewChoiceItemCount6")],
            ReqCreatureOrGoId = [I(r, "ReqCreatureOrGOId1"), I(r, "ReqCreatureOrGOId2"), I(r, "ReqCreatureOrGOId3"), I(r, "ReqCreatureOrGOId4")],
            ReqCreatureOrGoCount = [U(r, "ReqCreatureOrGOCount1"), U(r, "ReqCreatureOrGOCount2"), U(r, "ReqCreatureOrGOCount3"), U(r, "ReqCreatureOrGOCount4")],
            ReqItemId = [U(r, "ReqItemId1"), U(r, "ReqItemId2"), U(r, "ReqItemId3"), U(r, "ReqItemId4"), U(r, "ReqItemId5"), U(r, "ReqItemId6")],
            ReqItemCount = [U(r, "ReqItemCount1"), U(r, "ReqItemCount2"), U(r, "ReqItemCount3"), U(r, "ReqItemCount4"), U(r, "ReqItemCount5"), U(r, "ReqItemCount6")],
            ObjectiveText = [S(r, "ObjectiveText1"), S(r, "ObjectiveText2"), S(r, "ObjectiveText3"), S(r, "ObjectiveText4")],
        };
    }
}
