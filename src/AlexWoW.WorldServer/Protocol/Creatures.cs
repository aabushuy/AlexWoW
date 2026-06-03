namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Шаблон существа — минимум для отображения и ответа на <c>CMSG_CREATURE_QUERY</c>.
/// Аналог строки <c>creature_template</c> в дампе мира (позже M5.4 подтянем из MySQL).
/// </summary>
public sealed record CreatureTemplate(
    uint Entry,
    string Name,
    string SubName,
    uint DisplayId,
    byte Level,
    uint Faction,
    uint UnitType); // CreatureType: 7 = Humanoid

/// <summary>Конкретный спавн существа в мире (шаблон + GUID + позиция).</summary>
public sealed record NpcSpawn(ulong Guid, CreatureTemplate Template, float X, float Y, float Z, float O);

/// <summary>Реестр существ и помощники по GUID (M5.1 — пока один тестовый NPC).</summary>
public static class Npcs
{
    /// <summary>HIGHGUID_UNIT (3.3.5a) — старшие 16 бит GUID существа.</summary>
    public const ulong HighGuidUnit = 0xF130;

    /// <summary>
    /// Сборка GUID существа: <c>0xF130 | entry &lt;&lt; 24 | counter</c>.
    /// counter — низкий 24-битный id спавна (в дампе — поле guid таблицы creature).
    /// </summary>
    public static ulong UnitGuid(uint entry, uint counter)
        => (HighGuidUnit << 48) | ((ulong)entry << 24) | counter;

    /// <summary>
    /// Тестовый NPC: модель цыплёнка (display 257 — настоящая creature-модель со своей
    /// текстурой, в отличие от моделей рас, которые без кастомизации рисуются белыми).
    /// Временный плейсхолдер: реальные модели придут с дампом мира (M5.4).
    /// </summary>
    public static readonly CreatureTemplate TestDummy = new(
        Entry: 190000,
        Name: "Тестовый цыплёнок",
        SubName: "AlexWoW",
        DisplayId: 257,
        Level: 1,
        Faction: 35,   // «дружелюбен ко всем» — нейтральный, не атакует
        UnitType: 8);  // Critter

    private static readonly Dictionary<uint, CreatureTemplate> ByEntry = new()
    {
        [TestDummy.Entry] = TestDummy,
    };

    public static CreatureTemplate? Find(uint entry)
        => ByEntry.TryGetValue(entry, out var template) ? template : null;
}
