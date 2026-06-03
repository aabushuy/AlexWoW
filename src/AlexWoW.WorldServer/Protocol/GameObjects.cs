namespace AlexWoW.WorldServer.Protocol;

/// <summary>Шаблон гейм-объекта (минимум для отображения и SMSG_GAMEOBJECT_QUERY_RESPONSE).</summary>
public sealed record GoTemplate(
    uint Entry, uint Type, uint DisplayId, string Name, uint Faction, uint Flags, float Size);

/// <summary>Спавн гейм-объекта: шаблон + GUID + позиция + кватернион поворота.</summary>
public sealed record GoSpawn(
    ulong Guid, GoTemplate Template,
    float X, float Y, float Z, float O,
    float Rot0, float Rot1, float Rot2, float Rot3);

/// <summary>Помощники по GUID гейм-объектов.</summary>
public static class GameObjects
{
    /// <summary>HIGHGUID_GAMEOBJECT (3.3.5a).</summary>
    public const ulong HighGuidGameObject = 0xF110;

    /// <summary>GUID гейм-объекта: <c>0xF110 | entry&lt;&lt;24 | counter</c> (counter = gameobject.guid).</summary>
    public static ulong GameObjectGuid(uint entry, uint counter)
        => (HighGuidGameObject << 48) | ((ulong)entry << 24) | counter;
}
