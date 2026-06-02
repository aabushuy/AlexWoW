namespace AlexWoW.WorldServer.Protocol;

/// <summary>Стартовая позиция персонажа (карта/зона/координаты).</summary>
public readonly record struct StartPosition(uint Map, uint Zone, float X, float Y, float Z);

/// <summary>
/// Стандартные стартовые точки по расам (значения mangos playercreateinfo, 3.3.5a).
/// Нужны для корректного фона на экране выбора персонажа (и спавна в M4).
/// </summary>
public static class StartPositions
{
    // race id → позиция
    private static readonly Dictionary<byte, StartPosition> Map = new()
    {
        [1] = new(0, 12, -8949.95f, -132.493f, 83.5312f),       // Human — Elwynn
        [2] = new(1, 14, -618.518f, -4251.67f, 38.718f),        // Orc — Durotar
        [3] = new(0, 1, -6240.32f, 331.033f, 382.758f),         // Dwarf — Dun Morogh
        [4] = new(1, 141, 10311.3f, 832.463f, 1326.41f),        // Night Elf — Teldrassil
        [5] = new(0, 85, 1676.71f, 1678.31f, 121.67f),          // Undead — Tirisfal
        [6] = new(1, 215, -2917.58f, -257.98f, 52.9968f),       // Tauren — Mulgore
        [7] = new(0, 1, -6240.32f, 331.033f, 382.758f),         // Gnome — Dun Morogh
        [8] = new(1, 14, -618.518f, -4251.67f, 38.718f),        // Troll — Durotar
        [10] = new(530, 3431, 10349.6f, -6357.29f, 33.4026f),   // Blood Elf — Sunstrider Isle
        [11] = new(530, 3526, -3961.64f, -13931.2f, 100.615f),  // Draenei — Ammen Vale
    };

    public static StartPosition ForRace(byte race)
        => Map.TryGetValue(race, out var pos) ? pos : new StartPosition(0, 12, -8949.95f, -132.493f, 83.5312f);
}
