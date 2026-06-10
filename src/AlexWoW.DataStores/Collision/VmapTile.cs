using System.Buffers.Binary;
using System.Numerics;

namespace AlexWoW.DataStores.Collision;

/// <summary>
/// Коллизионная геометрия одного тайла (треугольники в ИГРОВЫХ координатах). Формат AlexWoW:
/// magic "AVMP" + u32 version + u32 triangleCount + count×(9 float = 3 вершины). Запросы —
/// перебор треугольников (Möller–Trumbore). Для редких LoS-проверок достаточно; BVH — позже.
/// </summary>
public sealed class VmapTile
{
    private const uint Magic = 0x504D5641; // 'A','V','M','P'
    private readonly float[] _v;           // 9 float на треугольник

    private VmapTile(float[] v) => _v = v;

    public int TriangleCount => _v.Length / 9;

    public static VmapTile? Load(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic)
                return null;
            var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
            var v = new float[count * 9];
            var src = bytes.AsSpan(12);
            for (var i = 0; i < v.Length; i++)
                v[i] = BinaryPrimitives.ReadSingleLittleEndian(src[(i * 4)..]);
            return new VmapTile(v);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Пересекает ли отрезок a→b какой-либо треугольник (строго между концами).</summary>
    public bool SegmentBlocked(Vector3 a, Vector3 b)
    {
        var dir = b - a;
        var len = dir.Length();
        if (len < 1e-4f)
            return false;
        dir /= len;

        for (var i = 0; i < _v.Length; i += 9)
        {
            var v0 = new Vector3(_v[i], _v[i + 1], _v[i + 2]);
            var v1 = new Vector3(_v[i + 3], _v[i + 4], _v[i + 5]);
            var v2 = new Vector3(_v[i + 6], _v[i + 7], _v[i + 8]);
            if (RayTriangle(a, dir, v0, v1, v2, out var t) && t > 0.1f && t < len - 0.1f)
                return true;
        }
        return false;
    }

    /// <summary>Наибольшая высота треугольника под точкой (x,y) не выше z+2; NaN — нет. Для «пола» зданий.</summary>
    public float FloorBelow(float x, float y, float z)
    {
        var best = float.NaN;
        var origin = new Vector3(x, y, z + 2f);
        var down = new Vector3(0, 0, -1);
        for (var i = 0; i < _v.Length; i += 9)
        {
            var v0 = new Vector3(_v[i], _v[i + 1], _v[i + 2]);
            var v1 = new Vector3(_v[i + 3], _v[i + 4], _v[i + 5]);
            var v2 = new Vector3(_v[i + 6], _v[i + 7], _v[i + 8]);
            if (RayTriangle(origin, down, v0, v1, v2, out var t) && t >= 0)
            {
                var hitZ = origin.Z - t;
                if (float.IsNaN(best) || hitZ > best)
                    best = hitZ;
            }
        }
        return best;
    }

    // Möller–Trumbore; t — расстояние вдоль dir (единичный).
    private static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0;
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(d, e2);
        var det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-7f)
            return false;
        var inv = 1f / det;
        var tv = o - a;
        var u = Vector3.Dot(tv, p) * inv;
        if (u < 0f || u > 1f)
            return false;
        var q = Vector3.Cross(tv, e1);
        var v = Vector3.Dot(d, q) * inv;
        if (v < 0f || u + v > 1f)
            return false;
        t = Vector3.Dot(e2, q) * inv;
        return t >= 0f;
    }
}
