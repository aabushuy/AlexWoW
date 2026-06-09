namespace AlexWoW.Database.Models;

/// <summary>Шаблон гейм-объекта из БД мира (для CMSG_GAMEOBJECT_QUERY).</summary>
public sealed record GameObjectTemplateData
{
    public uint Entry { get; init; }
    public uint Type { get; init; }
    public uint DisplayId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? IconName { get; init; }
    public string? CastBarCaption { get; init; }
    public string? Unk1 { get; init; }
    public float Size { get; init; }
}
