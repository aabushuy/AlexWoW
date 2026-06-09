using MySqlConnector;

namespace AlexWoW.Database.Repositories.World;

/// <summary>
/// База для focused-репозиториев read-only БД мира (дамп CMaNGOS, БД <c>mangos</c>): открытие
/// короткоживущего соединения на запрос (как было в едином WorldDatabase). Рефактор #25 (SOLID):
/// единая инфраструктура подключения, чтобы каждый репозиторий отвечал только за свою область.
/// </summary>
public abstract class MangosRepositoryBase(string connectionString)
{
    private readonly string _connectionString = connectionString;

    protected async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
