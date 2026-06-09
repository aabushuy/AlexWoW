using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Репозиторий аккаунтов (таблица account, БД <c>alexwow_auth</c>) — только операции с аккаунтами.
/// Список реалмов вынесен в <see cref="IRealmRepository"/>, инициализация схемы — в
/// <see cref="ISchemaInitializer"/> (SRP, рефактор #24).
/// </summary>
public interface IAccountRepository
{
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
}
