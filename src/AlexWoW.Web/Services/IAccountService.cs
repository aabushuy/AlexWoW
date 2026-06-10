using AlexWoW.Database.Models;

namespace AlexWoW.Web.Services;

/// <summary>Исход регистрации аккаунта.</summary>
public enum RegisterResult
{
    Success,
    AccountNameTaken,
    EmailTaken,
}

/// <summary>
/// Прикладная логика аккаунтов веб-панели: регистрация, проверка пароля и его смена.
/// Пароль НЕ хранится — только SRP6 соль+верификатор. ВАЖНО: SRP-верификатор считается по
/// ИГРОВОМУ имени аккаунта (его игрок вводит в клиенте WoW), а НЕ по email — клиент режет логин
/// по «@». Email используется только для входа на сайт.
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// Создаёт аккаунт: <paramref name="accountName"/> — игровой логин (SRP), <paramref name="email"/> —
    /// для входа на сайт. Новые SRP6 соль+верификатор по игровому имени.
    /// </summary>
    Task<RegisterResult> RegisterAsync(string email, string accountName, string password,
        CancellationToken ct = default);

    /// <summary>Проверяет пару email/пароль (поиск по email, SRP по игровому имени). Аккаунт либо null.</summary>
    Task<Account?> VerifyCredentialsAsync(string email, string password, CancellationToken ct = default);

    /// <summary>
    /// Меняет пароль: сверяет текущий, затем пишет новые соль+верификатор (сбрасывает session_key).
    /// Возвращает false, если текущий пароль неверен или аккаунт не найден.
    /// </summary>
    Task<bool> ChangePasswordAsync(string email, string currentPassword, string newPassword,
        CancellationToken ct = default);
}
