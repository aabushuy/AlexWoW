namespace AlexWoW.Database.Models;

/// <summary>Точка телепорта dev-меню (строка <c>dev_teleport</c>). Иммутабельный DTO для каталога меню.</summary>
public sealed record TeleportLocation
{
    public uint Id { get; init; }
    public int SortOrder { get; init; }
    public required string Name { get; init; }
    /// <summary>0 = нейтрал/обе фракции, 1 = Альянс, 2 = Орда.</summary>
    public byte Faction { get; init; }
    public uint Map { get; init; }
    public uint Zone { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float O { get; init; }
}
