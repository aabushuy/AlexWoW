namespace AlexWoW.Database.Models;

/// <summary>Шаблон существа из БД мира (для ответа на CMSG_CREATURE_QUERY).</summary>
public sealed record CreatureTemplateData
{
    public uint Entry { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? SubName { get; init; }
    public uint DisplayId1 { get; init; }
    public ushort Faction { get; init; }
    public byte MinLevel { get; init; }
    public byte CreatureType { get; init; }
    public uint NpcFlags { get; init; }
    public byte UnitClass { get; init; }
    public float Scale { get; init; }
}
