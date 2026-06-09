using AlexWoW.Database.Models;
using Dapper;
using MySqlConnector;

namespace AlexWoW.Database;

/// <summary>
/// Доступ к базе аутентификации (аккаунты + список реалмов). Использует Dapper поверх MySqlConnector.
/// </summary>
public sealed class AuthDatabase(string connectionString)
{
    private readonly string _connectionString = connectionString;

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    /// <summary>Создаёт таблицы, если их нет, и сидирует реалм по умолчанию.</summary>
    public async Task EnsureSchemaAsync(Realm defaultRealm, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS account (
                id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
                username    VARCHAR(32)  NOT NULL,
                salt        BINARY(32)   NOT NULL,
                verifier    BINARY(32)   NOT NULL,
                session_key BINARY(40)   NULL,
                last_ip     VARCHAR(45)  NULL,
                is_admin    TINYINT UNSIGNED NOT NULL DEFAULT 0,
                created_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (id),
                UNIQUE KEY uk_account_username (username)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        // M7: флаг администратора (доступ к DevCommands). Для существующих таблиц — ALTER (1060 = дубликат).
        try
        {
            await db.ExecuteAsync("ALTER TABLE account ADD COLUMN is_admin TINYINT UNSIGNED NOT NULL DEFAULT 0;");
        }
        catch (MySqlException ex) when (ex.Number == 1060) { /* столбец уже есть */ }

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS realmlist (
                id         INT UNSIGNED NOT NULL AUTO_INCREMENT,
                name       VARCHAR(64)  NOT NULL,
                address    VARCHAR(64)  NOT NULL,
                port       SMALLINT UNSIGNED NOT NULL DEFAULT 8085,
                type       TINYINT UNSIGNED NOT NULL DEFAULT 0,
                flags      TINYINT UNSIGNED NOT NULL DEFAULT 0,
                timezone   TINYINT UNSIGNED NOT NULL DEFAULT 1,
                population FLOAT        NOT NULL DEFAULT 0,
                PRIMARY KEY (id),
                UNIQUE KEY uk_realmlist_name (name)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        var realmCount = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM realmlist;");
        if (realmCount == 0)
        {
            await db.ExecuteAsync("""
                INSERT INTO realmlist (name, address, port, type, flags, timezone, population)
                VALUES (@Name, @Address, @Port, @Type, @Flags, @Timezone, @Population);
                """, defaultRealm);
        }
    }

    public async Task<Account?> GetAccountByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<Account>("""
            SELECT id AS Id, username AS Username, salt AS Salt, verifier AS Verifier,
                   session_key AS SessionKey, last_ip AS LastIp, is_admin AS IsAdmin
            FROM account WHERE username = @username;
            """, new { username = username.ToUpperInvariant() });
    }

    public async Task<bool> AccountExistsAsync(string username, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var count = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM account WHERE username = @username;",
            new { username = username.ToUpperInvariant() });
        return count > 0;
    }

    public async Task CreateAccountAsync(string username, byte[] salt, byte[] verifier, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync("""
            INSERT INTO account (username, salt, verifier)
            VALUES (@username, @salt, @verifier);
            """, new { username = username.ToUpperInvariant(), salt, verifier });
    }

    /// <summary>Все имена аккаунтов (для массовых операций, напр. сброса пароля).</summary>
    public async Task<IReadOnlyList<string>> GetAllUsernamesAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<string>("SELECT username FROM account;");
        return rows.AsList();
    }

    /// <summary>Меняет пароль аккаунта (новые соль+верификатор SRP6); сбрасывает session_key (форс ре-логин).</summary>
    public async Task UpdatePasswordAsync(string username, byte[] salt, byte[] verifier, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync("""
            UPDATE account SET salt = @salt, verifier = @verifier, session_key = NULL
            WHERE username = @username;
            """, new { username = username.ToUpperInvariant(), salt, verifier });
    }

    public async Task SetSessionKeyAsync(uint accountId, byte[] sessionKey, string? ip, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync("""
            UPDATE account SET session_key = @sessionKey, last_ip = @ip WHERE id = @accountId;
            """, new { accountId, sessionKey, ip });
    }

    /// <summary>Ставит/снимает флаг администратора аккаунту. Возвращает число затронутых строк. M7.</summary>
    public async Task<int> SetAdminAsync(string username, bool isAdmin, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.ExecuteAsync("UPDATE account SET is_admin = @isAdmin WHERE username = @username;",
            new { username = username.ToUpperInvariant(), isAdmin = isAdmin ? 1 : 0 });
    }

    public async Task<IReadOnlyList<Realm>> GetRealmsAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var realms = await db.QueryAsync<Realm>("""
            SELECT id AS Id, name AS Name, address AS Address, port AS Port,
                   type AS Type, flags AS Flags, timezone AS Timezone, population AS Population
            FROM realmlist ORDER BY id;
            """);
        return realms.AsList();
    }
}
