namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>account_data</c> (UI-блобы клиента). Срез 2 рефактора DAL (#23).</summary>
public sealed class AccountDataBlob
{
    public uint OwnerId { get; set; }        // PK часть 1 (account_id ИЛИ guid персонажа)
    public byte IsChar { get; set; }         // PK часть 2 (0=глобальный, 1=per-character)
    public byte DataType { get; set; }       // PK часть 3 (0..7)
    public uint UpdateTime { get; set; }     // DEFAULT 0
    public byte[]? Data { get; set; }        // longblob, nullable
}
