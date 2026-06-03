using System.Collections.Concurrent;

namespace AlexWoW.DataStores.Terrain;

/// <summary>
/// Доступ к рельефу: по (mapId, x, y) находит нужный грид-файл <c>maps/MMMGGgg.map</c>,
/// лениво грузит и кэширует, отдаёт высоту земли. Потокобезопасно.
/// Грид: <c>gx = (int)(32 - x/SIZE)</c>, <c>gy = (int)(32 - y/SIZE)</c>, 64×64 на карту.
/// </summary>
public sealed class TerrainMaps(string mapsPath)
{
    private const int MaxGrids = 64;

    private readonly ConcurrentDictionary<(uint Map, int Gx, int Gy), GridMap?> _grids = new();

    /// <summary>Каталог с .map-файлами задан и существует.</summary>
    public bool Available => !string.IsNullOrWhiteSpace(mapsPath) && Directory.Exists(mapsPath);

    /// <summary>Высота земли в точке или null (нет данных/вне диапазона).</summary>
    public float? GetHeight(uint mapId, float x, float y)
    {
        if (!Available)
            return null;

        var gx = (int)(32 - x / GridMap.SizeOfGrids);
        var gy = (int)(32 - y / GridMap.SizeOfGrids);
        if (gx is < 0 or >= MaxGrids || gy is < 0 or >= MaxGrids)
            return null;

        var grid = _grids.GetOrAdd((mapId, gx, gy), key =>
            GridMap.Load(Path.Combine(mapsPath, $"{key.Map:D3}{key.Gx:D2}{key.Gy:D2}.map")));
        if (grid is null)
            return null;

        var h = grid.GetHeight(x, y);
        return h <= GridMap.InvalidHeight ? null : h;
    }
}
