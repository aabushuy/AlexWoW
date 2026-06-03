using AlexWoW.DataStores.Terrain;
using DotRecast.Detour;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset;
using DotRecast.Recast.Toolset.Builder;

namespace AlexWoW.MmapGen;

/// <summary>
/// Сборка навмеша одного тайла: рельеф (maps) как ходимая поверхность + vmap-треугольники (WMO)
/// как препятствия. Координаты Recast: Y вверх → вершина (worldX, height, worldY).
/// </summary>
public static class NavmeshBuild
{
    private const float S = 1600.0f / 3.0f;
    private const int N = 129; // вершин на сторону (как разрешение рельефа ADT)

    public static DtMeshData? BuildTile(TerrainMaps terrain, float[]? vmapTris, uint map, int gx, int gy)
    {
        var xMin = (31 - gx) * S; var xMax = (32 - gx) * S;
        var yMin = (31 - gy) * S; var yMax = (32 - gy) * S;
        var dx = (xMax - xMin) / (N - 1);
        var dy = (yMax - yMin) / (N - 1);

        var vmapCount = vmapTris?.Length ?? 0;            // float'ов всего (9 на треугольник)
        var verts = new float[N * N * 3 + vmapCount];     // terrain + vmap-вершины (как есть)
        var faces = new List<int>((N - 1) * (N - 1) * 6 + vmapCount / 9 * 3);

        // Рельеф
        for (var i = 0; i < N; i++)
        for (var j = 0; j < N; j++)
        {
            var wx = xMin + i * dx;
            var wy = yMin + j * dy;
            var h = terrain.GetHeight(map, wx, wy) ?? 0f;
            var vi = (i * N + j) * 3;
            verts[vi] = wx; verts[vi + 1] = h; verts[vi + 2] = wy;
        }
        int Vid(int i, int j) => i * N + j;
        for (var i = 0; i < N - 1; i++)
        for (var j = 0; j < N - 1; j++)
        {
            faces.Add(Vid(i, j)); faces.Add(Vid(i, j + 1)); faces.Add(Vid(i + 1, j));
            faces.Add(Vid(i + 1, j)); faces.Add(Vid(i, j + 1)); faces.Add(Vid(i + 1, j + 1));
        }

        // vmap-препятствия (WMO): game (x,y,z=height) → recast (x, z, y); препятствие рассечётся как solid.
        if (vmapTris is { Length: > 0 })
        {
            var baseV = N * N;
            var vbase = N * N * 3;
            for (var i = 0; i + 9 <= vmapTris.Length; i += 9)
            {
                for (var k = 0; k < 3; k++)
                {
                    var gxc = vmapTris[i + k * 3];
                    var gyc = vmapTris[i + k * 3 + 1];
                    var gzc = vmapTris[i + k * 3 + 2];
                    verts[vbase++] = gxc; verts[vbase++] = gzc; verts[vbase++] = gyc;
                }
                var v = baseV + i / 3;
                faces.Add(v); faces.Add(v + 1); faces.Add(v + 2);
            }
        }

        var geom = new RcSampleInputGeomProvider(verts, faces.ToArray());
        var settings = new RcNavMeshBuildSettings
        {
            cellSize = 0.3f, cellHeight = 0.2f,
            agentHeight = 2.0f, agentRadius = 0.6f, agentMaxClimb = 0.9f, agentMaxSlope = 50f,
        };
        var result = new SoloNavMeshBuilder().Build(geom, settings);
        if (!result.Success || result.NavMesh is null)
            return null;
        return result.NavMesh.GetTile(0).data;
    }
}
