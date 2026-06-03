using AlexWoW.DataStores.Terrain;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset;
using DotRecast.Recast.Toolset.Builder;

// Навмеш одного тайла из рельефа: tile <mapsDir> <map> <gx> <gy>
// Recast: Y вверх → вершина = (worldX, height, worldY). Намотка — нормалью вверх.
if (args.Length >= 5 && args[0].Equals("tile", StringComparison.OrdinalIgnoreCase))
{
    const float S = 1600.0f / 3.0f;
    var terrain = new TerrainMaps(args[1]);
    var map = uint.Parse(args[2]);
    var gx = int.Parse(args[3]);
    var gy = int.Parse(args[4]);

    var xMin = (31 - gx) * S; var xMax = (32 - gx) * S;
    var yMin = (31 - gy) * S; var yMax = (32 - gy) * S;
    const int N = 129;
    var dx = (xMax - xMin) / (N - 1);
    var dy = (yMax - yMin) / (N - 1);

    var verts = new float[N * N * 3];
    for (var i = 0; i < N; i++)
    for (var j = 0; j < N; j++)
    {
        var wx = xMin + i * dx;
        var wy = yMin + j * dy;
        var h = terrain.GetHeight(map, wx, wy) ?? 0f;
        var vi = (i * N + j) * 3;
        verts[vi] = wx; verts[vi + 1] = h; verts[vi + 2] = wy; // (x, up=height, z)
    }

    var faces = new List<int>((N - 1) * (N - 1) * 6);
    int Vid(int i, int j) => i * N + j;
    for (var i = 0; i < N - 1; i++)
    for (var j = 0; j < N - 1; j++)
    {
        // намотка под нормаль +Y (вверх)
        faces.Add(Vid(i, j)); faces.Add(Vid(i, j + 1)); faces.Add(Vid(i + 1, j));
        faces.Add(Vid(i + 1, j)); faces.Add(Vid(i, j + 1)); faces.Add(Vid(i + 1, j + 1));
    }

    var geom = new RcSampleInputGeomProvider(verts, faces.ToArray());
    var settings = new RcNavMeshBuildSettings
    {
        cellSize = 0.3f, cellHeight = 0.2f,
        agentHeight = 2.0f, agentRadius = 0.6f, agentMaxClimb = 0.9f, agentMaxSlope = 50f,
    };
    var result = new SoloNavMeshBuilder().Build(geom, settings);
    var npolys = result.RecastBuilderResults?.Sum(r => r.Mesh?.npolys ?? 0) ?? 0;
    Console.WriteLine($"Навмеш тайла {map}/{gx},{gy}: success={result.Success}, polys={npolys}");
    if (!result.Success || result.NavMesh is null)
        return;

    var query = new DtNavMeshQuery(result.NavMesh);
    var filter = new DtQueryDefaultFilter();
    var he = new RcVec3f(4f, 8f, 4f);

    RcVec3f ToRec(float wx, float wy) => new(wx, (terrain.GetHeight(map, wx, wy) ?? 0f), wy);
    var a = ToRec(-8949f, -132f);   // старт человека
    var b = ToRec(-8900f, -185f);   // у аббатства
    query.FindNearestPoly(a, he, filter, out var ra, out var na, out _);
    query.FindNearestPoly(b, he, filter, out var rb, out var nb, out _);
    Console.WriteLine($"polyA={ra} polyB={rb}");
    if (ra != 0 && rb != 0)
    {
        Span<long> path = new long[256];
        var st = query.FindPath(ra, rb, na, nb, filter, path, out var pc, 256);
        Console.WriteLine($"FindPath: status={st}, polys={pc}  (A→B путь по навмешу)");
    }
    return;
}

Console.WriteLine("Использование: tile <mapsDir> <map> <gx> <gy>");
