namespace AlexWoW.Database.Models;

/// <summary>Игровой аккаунт. Пароль не хранится — только SRP6-соль и верификатор.</summary>
public sealed class Account
{
    public uint Id { get; init; }
    public required string Username { get; init; }  // игровой логин (вводится в клиенте WoW)
    public string? Email { get; init; }              // M8: вход на сайт; null у CLI/игровых аккаунтов
    public required byte[] Salt { get; init; }      // 32 байта little-endian
    public required byte[] Verifier { get; init; }  // 32 байта little-endian
    public byte[]? SessionKey { get; set; }         // 40 байт, обновляется при логине
    public string? LastIp { get; set; }
    public bool IsAdmin { get; init; }              // M7: доступ к дев/GM-командам (DevCommands)
    public DateTime CreatedAt { get; init; }        // M8.8: дата регистрации (account.created_at)
}
