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

    /// <summary>Аккаунт по email (вход на сайт, M8). Сравнение без учёта регистра.</summary>
    Task<Account?> GetAccountByEmailAsync(string email, CancellationToken ct = default);

    Task<bool> AccountExistsAsync(string username, CancellationToken ct = default);

    /// <summary>Занят ли email (M8). Сравнение без учёта регистра.</summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);

    /// <summary>Создаёт аккаунт. <paramref name="email"/> — для входа на сайт (M8), null у игровых/CLI.</summary>
    Task CreateAccountAsync(string username, byte[] salt, byte[] verifier, string? email = null,
        CancellationToken ct = default);

    /// <summary>Все имена аккаунтов (для массовых операций, напр. сброса пароля).</summary>
    Task<IReadOnlyList<string>> GetAllUsernamesAsync(CancellationToken ct = default);

    /// <summary>Имена админских аккаунтов (для дропдауна «Исполнитель» на доске, KB-фикс).</summary>
    Task<IReadOnlyList<string>> GetAdminUsernamesAsync(CancellationToken ct = default);

    /// <summary>Сводка всех аккаунтов + число персонажей (админ-список, M8.9). Сортировка по имени.</summary>
    Task<IReadOnlyList<AccountSummary>> GetAccountsWithCharCountsAsync(CancellationToken ct = default);

    /// <summary>Аккаунт по id или null (админ-карточка, M8.9).</summary>
    Task<Account?> GetAccountByIdAsync(uint id, CancellationToken ct = default);

    /// <summary>Меняет пароль аккаунта (новые соль+верификатор SRP6); сбрасывает session_key.</summary>
    Task UpdatePasswordAsync(string username, byte[] salt, byte[] verifier, CancellationToken ct = default);

    Task SetSessionKeyAsync(uint accountId, byte[] sessionKey, string? ip, CancellationToken ct = default);

    /// <summary>Ставит/снимает флаг администратора аккаунту. Возвращает число затронутых строк.</summary>
    Task<int> SetAdminAsync(string username, bool isAdmin, CancellationToken ct = default);
}
