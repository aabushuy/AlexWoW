using System.Collections.Concurrent;
using System.Numerics;

namespace AlexWoW.DataStores.Collision;

/// <summary>
/// Доступ к коллизиям (vmap): по (map,x,y) грузит тайл `{map:D3}{gx:D2}{gy:D2}.vmap`, кэширует.
/// LoS и «пол» зданий/мостов. Координаты — игровые (как maps/крич). gx=(int)(32 - x/533.333).
/// </summary>
public sealed class Vmaps(string vmapsPath)
{
    private const float SizeOfGrids = 1600.0f / 3.0f;
    private const int MaxGrids = 64;

    private readonly ConcurrentDictionary<(uint Map, int Gx, int Gy), VmapTile?> _tiles = new();

    public bool Available => !string.IsNullOrWhiteSpace(vmapsPath) && Directory.Exists(vmapsPath);

    /// <summary>true — между точками нет препятствий (или vmap недоступен).</summary>
    public bool IsInLineOfSight(uint map, float x1, float y1, float z1, float x2, float y2, float z2)
    {
        if (!Available)
            return true;

        var a = new Vector3(x1, y1, z1);
        var b = new Vector3(x2, y2, z2);
        foreach (var (gx, gy) in TilesForSegment(x1, y1, x2, y2))
        {
            var tile = GetTile(map, gx, gy);
            if (tile is not null && tile.SegmentBlocked(a, b))
                return false;
        }
        return true;
    }

    /// <summary>Высота «пола» (здание/мост) под точкой или null.</summary>
    public float? GetFloor(uint map, float x, float y, float z)
    {
        if (!Available)
            return null;
        var tile = GetTile(map, Grid(x), Grid(y));
        if (tile is null)
            return null;
        var f = tile.FloorBelow(x, y, z);
        return float.IsNaN(f) ? null : f;
    }

    private VmapTile? GetTile(uint map, int gx, int gy)
    {
        if (gx is < 0 or >= MaxGrids || gy is < 0 or >= MaxGrids)
            return null;
        return _tiles.GetOrAdd((map, gx, gy), k =>
            VmapTile.Load(Path.Combine(vmapsPath, $"{k.Map:D3}{k.Gx:D2}{k.Gy:D2}.vmap")));
    }

    private static int Grid(float c) => (int)(32 - c / SizeOfGrids);

    private static IEnumerable<(int Gx, int Gy)> TilesForSegment(float x1, float y1, float x2, float y2)
    {
        var gx1 = Grid(MathF.Max(x1, x2));
        var gx2 = Grid(MathF.Min(x1, x2));
        var gy1 = Grid(MathF.Max(y1, y2));
        var gy2 = Grid(MathF.Min(y1, y2));
        for (var gx = gx1; gx <= gx2; gx++)
        {
            for (var gy = gy1; gy <= gy2; gy++)
                yield return (gx, gy);
        }
    }
}
