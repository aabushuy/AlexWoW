namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>account</c> (БД alexwow_auth). Срез 2 рефактора DAL (#23).</summary>
public sealed class Account
{
    public uint Id { get; set; }
    public string Username { get; set; } = null!;          // игровой логин (вводится в клиенте WoW)
    public string? Email { get; set; }                      // M8: вход на сайт; null у CLI/игровых аккаунтов
    public byte[] Salt { get; set; } = null!;       // binary(32)
    public byte[] Verifier { get; set; } = null!;   // binary(32)
    public byte[]? SessionKey { get; set; }          // binary(40), nullable
    public string? LastIp { get; set; }              // varchar(45), nullable
    public DateTime CreatedAt { get; set; }          // timestamp DEFAULT CURRENT_TIMESTAMP
    public byte IsAdmin { get; set; }                // tinyint unsigned DEFAULT 0
}
