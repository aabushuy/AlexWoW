using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Репозиторий БД аутентификации (аккаунты + список реалмов, БД <c>alexwow_auth</c>).
/// Срез 1 рефактора DAL (#23): абстракция поверх существующей Dapper-реализации
/// (<see cref="AuthDatabase"/>); поведение не меняется.
/// </summary>
public interface IAccountRepository
{
    /// <summary>Создаёт таблицы, если их нет, и сидирует реалм по умолчанию.</summary>
    Task EnsureSchemaAsync(Realm defaultRealm, CancellationToken ct = default);

    Task<Account?> GetAccountByUsernameAsync(string username, CancellationToken ct = default);

    Task<bool> AccountExistsAsync(string username, CancellationToken ct = default);

    Task CreateAccountAsync(string username, byte[] salt, byte[] verifier, CancellationToken ct = default);

    /// <summary>Все имена аккаунтов (для массовых операций, напр. сброса пароля).</summary>
    Task<IReadOnlyList<string>> GetAllUsernamesAsync(CancellationToken ct = default);

    /// <summary>Меняет пароль аккаунта (новые соль+верификатор SRP6); сбрасывает session_key.</summary>
    Task UpdatePasswordAsync(string username, byte[] salt, byte[] verifier, CancellationToken ct = default);

    Task SetSessionKeyAsync(uint accountId, byte[] sessionKey, string? ip, CancellationToken ct = default);

    /// <summary>Ставит/снимает флаг администратора аккаунту. Возвращает число затронутых строк.</summary>
    Task<int> SetAdminAsync(string username, bool isAdmin, CancellationToken ct = default);

    Task<IReadOnlyList<Realm>> GetRealmsAsync(CancellationToken ct = default);
}
