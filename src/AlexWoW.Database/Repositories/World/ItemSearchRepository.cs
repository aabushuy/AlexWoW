using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>Поиск предметов по <c>item_template</c> для админки. SRP-репозиторий (#25), Dapper read-only.</summary>
public sealed class ItemSearchRepository(string connectionString)
    : MangosRepositoryBase(connectionString), IItemSearchRepository
{
    public async Task<IReadOnlyList<ItemTemplateData>> SearchAsync(
        ItemSearchFilter filter, CancellationToken ct = default)
    {
        var parameters = new DynamicParameters();
        var where = filter.BuildWhere(parameters);
        parameters.Add("limit", Math.Clamp(filter.Limit, 1, 500));

        var order = filter.OrderByItemLevel ? "ItemLevel DESC, name" : "RequiredLevel, name";
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync(new CommandDefinition(
            $"SELECT * FROM item_template WHERE {where} ORDER BY {order} LIMIT @limit;",
            parameters, cancellationToken: ct));

        var result = new List<ItemTemplateData>();
        foreach (var row in rows)
            result.Add(ItemTemplateMapper.Map((IDictionary<string, object>)row));
        return result;
    }
}
