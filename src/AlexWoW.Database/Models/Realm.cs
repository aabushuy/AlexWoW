namespace AlexWoW.Database.Models;

/// <summary>Описание реалма (игрового мира) для списка выбора в клиенте.</summary>
public sealed record Realm
{
    public uint Id { get; init; }
    public required string Name { get; init; }
    /// <summary>IP-адрес world-сервера, видимый клиенту (например, 192.168.2.210).</summary>
    public required string Address { get; init; }
    public ushort Port { get; init; }
    /// <summary>Иконка/тип: 0 = Normal (PvE), 1 = PvP, 4 = Normal, 6 = RP, 8 = RP PvP.</summary>
    public byte Type { get; init; }
    /// <summary>Флаги реалма: 0 = нет, 0x02 = offline, 0x40 = recommended и т.д.</summary>
    public byte Flags { get; init; }
    public byte Timezone { get; init; }
    /// <summary>Заполненность: 0.0 = low, 1.0 = medium, 2.0 = high.</summary>
    public float Population { get; init; }
}
