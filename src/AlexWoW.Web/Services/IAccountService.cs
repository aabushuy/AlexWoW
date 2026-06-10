using AlexWoW.Database.Models;

namespace AlexWoW.Web.Services;

/// <summary>Исход регистрации аккаунта.</summary>
public enum RegisterResult
{
    Success,
    AlreadyExists,
}

/// <summary>
/// Прикладная логика аккаунтов веб-панели: регистрация, проверка пароля и его смена.
/// Пароль НЕ хранится — только SRP6 соль+верификатор (как игровой логин). Логин = email,
/// имя аккаунта в БД = email в верхнем регистре (совпадает с CLI <c>create-account</c>).
/// </summary>
public interface IAccountService
{
    /// <summary>Создаёт аккаунт (email → username upper, новые SRP6 соль+верификатор).</summary>
    Task<RegisterResult> RegisterAsync(string email, string password, CancellationToken ct = default);

    /// <summary>Проверяет пару email/пароль пересчётом верификатора по соли. Возвращает аккаунт либо null.</summary>
    Task<Account?> VerifyCredentialsAsync(string email, string password, CancellationToken ct = default);

    /// <summary>
    /// Меняет пароль: сверяет текущий, затем пишет новые соль+верификатор (сбрасывает session_key).
    /// Возвращает false, если текущий пароль неверен или аккаунт не найден.
    /// </summary>
    Task<bool> ChangePasswordAsync(string email, string currentPassword, string newPassword,
        CancellationToken ct = default);
}
