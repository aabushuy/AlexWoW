namespace AlexWoW.Database.Models;

/// <summary>
/// Сводка аккаунта для админ-списка (M8.9): идентификация + дата регистрации + число персонажей.
/// Пароль/соль/верификатор сюда не входят — только то, что показываем в таблице аккаунтов.
/// </summary>
public sealed class AccountSummary
{
    public uint Id { get; init; }
    public required string Username { get; init; }
    public string? Email { get; init; }
    public DateTime CreatedAt { get; init; }
    public int CharacterCount { get; init; }
}
