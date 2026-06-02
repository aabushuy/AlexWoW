namespace AlexWoW.Database.Models;

/// <summary>Игровой аккаунт. Пароль не хранится — только SRP6-соль и верификатор.</summary>
public sealed class Account
{
    public uint Id { get; init; }
    public required string Username { get; init; }
    public required byte[] Salt { get; init; }      // 32 байта little-endian
    public required byte[] Verifier { get; init; }  // 32 байта little-endian
    public byte[]? SessionKey { get; set; }         // 40 байт, обновляется при логине
    public string? LastIp { get; set; }
}
