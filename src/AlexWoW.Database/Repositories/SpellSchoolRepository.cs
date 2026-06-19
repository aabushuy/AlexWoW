using AlexWoW.Database.Abstractions;
using Dapper;
using MySqlConnector;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// Dapper-доступ к <c>mangos.spell_template.SchoolMask</c> (KB14). Минимальный read-only репозиторий для
/// сортировки regression-списка аддона по школе. Пустая строка подключения / пустой список ids → пусто.
/// </summary>
public sealed class SpellSchoolRepository(string connectionString) : ISpellSchoolRepository
{
    private readonly string _cs = connectionString;

    public bool Configured => !string.IsNullOrWhiteSpace(_cs);

    public async Task<IReadOnlyDictionary<int, int>> GetSchoolMasksAsync(
        IReadOnlyCollection<int> spellIds, CancellationToken ct = default)
    {
        if (!Configured || spellIds.Count == 0) return new Dictionary<int, int>();
        await using var c = new MySqlConnection(_cs);
        await c.OpenAsync(ct);
        var rows = await c.QueryAsync<(int Id, int SchoolMask)>(new CommandDefinition(
            "SELECT Id, SchoolMask FROM mangos.spell_template WHERE Id IN @ids",
            new { ids = spellIds }, cancellationToken: ct));
        return rows.ToDictionary(r => r.Id, r => r.SchoolMask);
    }
}
