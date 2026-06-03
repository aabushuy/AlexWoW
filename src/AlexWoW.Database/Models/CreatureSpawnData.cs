namespace AlexWoW.Database.Models;

/// <summary>
/// Строка спавна существа из БД мира CMaNGOS (join creature + creature_template).
/// Сырые поля схемы дампа; маппинг в протокольный вид — на стороне WorldServer.
/// </summary>
public sealed class CreatureSpawnData
{
    public uint Guid { get; init; }    // creature.guid — низкий counter для GUID существа
    public uint Entry { get; init; }   // creature.id = creature_template.Entry
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float O { get; init; }

    public string Name { get; init; } = string.Empty;
    public string? SubName { get; init; }
    public uint DisplayId1 { get; init; }
    public uint DisplayId2 { get; init; }
    public uint DisplayId3 { get; init; }
    public uint DisplayId4 { get; init; }
    public ushort Faction { get; init; }
    public byte MinLevel { get; init; }
    public byte MaxLevel { get; init; }
    public byte CreatureType { get; init; }
    public uint NpcFlags { get; init; }
    public byte UnitClass { get; init; }
    public float Scale { get; init; }

    /// <summary>Первый ненулевой displayId (0, если модель не задана).</summary>
    public uint DisplayId =>
        DisplayId1 != 0 ? DisplayId1 :
        DisplayId2 != 0 ? DisplayId2 :
        DisplayId3 != 0 ? DisplayId3 : DisplayId4;
}

/// <summary>Шаблон существа из БД мира (для ответа на CMSG_CREATURE_QUERY).</summary>
public sealed class CreatureTemplateData
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
