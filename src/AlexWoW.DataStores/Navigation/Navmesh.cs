using System.Collections.Concurrent;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;

namespace AlexWoW.DataStores.Navigation;

/// <summary>
/// Навмеш для поиска пути (mmaps). По (map,x,y) грузит тайл `{map:D3}{gx:D2}{gy:D2}.mmtile`
/// (DtMeshData), строит DtNavMesh/DtNavMeshQuery, кэширует. Запрос — внутри тайла (интра-тайл AI).
/// Recast Y-вверх: точка game (x,y,z) → recast (x, z, y).
/// </summary>
public sealed class Navmesh(string mmapsPath)
{
    private const float S = 1600.0f / 3.0f;
    private const int MaxGrids = 64;
    private const int VertsPerPoly = 6;

    private readonly ConcurrentDictionary<(uint Map, int Gx, int Gy), DtNavMeshQuery?> _tiles = new();

    public bool Available => !string.IsNullOrWhiteSpace(mmapsPath) && Directory.Exists(mmapsPath);

    private static int Grid(float c) => (int)(32 - c / S);

    /// <summary>Путь от (start) до (end) в игровых координатах или null (нет навмеша/пути).</summary>
    public List<(float X, float Y, float Z)>? FindPath(uint map,
        float sx, float sy, float sz, float ex, float ey, float ez)
    {
        if (!Available)
            return null;
        var query = GetQuery(map, Grid(sx), Grid(sy));
        if (query is null)
            return null;

        var filter = new DtQueryDefaultFilter();
        var he = new RcVec3f(4f, 8f, 4f);
        var start = new RcVec3f(sx, sz, sy); // game(x,y,z) -> recast(x, z, y)
        var end = new RcVec3f(ex, ez, ey);

        query.FindNearestPoly(start, he, filter, out var sref, out var snear, out _);
        query.FindNearestPoly(end, he, filter, out var eref, out var enear, out _);
        if (sref == 0 || eref == 0)
            return null;

        Span<long> path = new long[256];
        var st = query.FindPath(sref, eref, snear, enear, filter, path, out var pc, 256);
        if (st.Failed() || pc == 0)
            return null;

        Span<DtStraightPath> straight = new DtStraightPath[256];
        query.FindStraightPath(snear, enear, path[..pc], pc, straight, out var spc, 256, 0);
        if (spc == 0)
            return null;

        var result = new List<(float, float, float)>(spc);
        for (var i = 0; i < spc; i++)
        {
            var p = straight[i].pos;     // recast (x, height, y)
            result.Add((p.X, p.Z, p.Y)); // -> game (x, y, z)
        }
        return result;
    }

    private DtNavMeshQuery? GetQuery(uint map, int gx, int gy)
    {
        if (gx is < 0 or >= MaxGrids || gy is < 0 or >= MaxGrids)
            return null;
        return _tiles.GetOrAdd((map, gx, gy), k =>
        {
            var path = Path.Combine(mmapsPath, $"{k.Map:D3}{k.Gx:D2}{k.Gy:D2}.mmtile");
            if (!File.Exists(path))
                return null;
            try
            {
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs);
                var data = new DtMeshDataReader().Read(br, VertsPerPoly);
                var navMesh = new DtNavMesh();
                if (navMesh.Init(data, VertsPerPoly, 0).Failed())
                    return null;
                return new DtNavMeshQuery(navMesh);
            }
            catch
            {
                return null;
            }
        });
    }
}
