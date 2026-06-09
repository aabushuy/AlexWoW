namespace AlexWoW.Database.Models;

/// <summary>Строка спавна гейм-объекта (join gameobject + gameobject_template).</summary>
public sealed record GameObjectSpawnData
{
    public uint Guid { get; init; }
    public uint Entry { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float O { get; init; }
    public float Rot0 { get; init; }
    public float Rot1 { get; init; }
    public float Rot2 { get; init; }
    public float Rot3 { get; init; }

    public string Name { get; init; } = string.Empty;
    public byte Type { get; init; }
    public uint DisplayId { get; init; }
    public ushort Faction { get; init; }
    public uint Flags { get; init; }
    public float Size { get; init; }
}
