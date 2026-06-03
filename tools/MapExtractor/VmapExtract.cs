using System.Buffers.Binary;
using System.Text;

namespace AlexWoW.MapExtractor;

public readonly record struct WmoPlacement(string Name, Vec3 Pos, Vec3 Rot, uint Flags);

/// <summary>
/// Размещения WMO из ADT (MWMO имена + MODF) и трансформация вершин модели в ИГРОВЫЕ координаты
/// (как у крич/высот). Цепочка: internal = R·v + fixCoords(pos); game = (mid−ix, mid−iy, iz).
/// </summary>
public static class VmapExtract
{
    public const float Mid = 0.5f * 64.0f * 533.33333f; // 17066.666

    public static List<WmoPlacement> ReadWmoPlacements(byte[] adt)
    {
        var names = new List<string>();
        var placements = new List<WmoPlacement>();

        var pos = 0;
        while (pos + 8 <= adt.Length)
        {
            var magic = ClientData.ReadReversedMagic(adt, pos);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(adt.AsSpan(pos + 4));
            var body = pos + 8;

            if (magic == "MWMO")
            {
                var p = body;
                var end = body + size;
                while (p < end)
                {
                    var z = p;
                    while (z < end && adt[z] != 0) z++;
                    if (z > p) names.Add(Encoding.ASCII.GetString(adt, p, z - p));
                    else names.Add(string.Empty);
                    p = z + 1;
                }
            }
            else if (magic == "MODF")
            {
                for (var i = 0; i + 64 <= size; i += 64)
                {
                    var e = body + i;
                    var nameId = (int)BinaryPrimitives.ReadUInt32LittleEndian(adt.AsSpan(e));
                    var px = BinaryPrimitives.ReadSingleLittleEndian(adt.AsSpan(e + 8));
                    var py = BinaryPrimitives.ReadSingleLittleEndian(adt.AsSpan(e + 12));
                    var pz = BinaryPrimitives.ReadSingleLittleEndian(adt.AsSpan(e + 16));
                    var rx = BinaryPrimitives.ReadSingleLittleEndian(adt.AsSpan(e + 20));
                    var ry = BinaryPrimitives.ReadSingleLittleEndian(adt.AsSpan(e + 24));
                    var rz = BinaryPrimitives.ReadSingleLittleEndian(adt.AsSpan(e + 28));
                    var flags = BinaryPrimitives.ReadUInt16LittleEndian(adt.AsSpan(e + 56));
                    if (nameId >= 0 && nameId < names.Count)
                        placements.Add(new WmoPlacement(names[nameId],
                            new Vec3(px, py, pz), new Vec3(rx, ry, rz), flags));
                }
            }
            pos = body + size;
        }
        return placements;
    }

    /// <summary>Вершина модели WMO → игровые координаты по размещению.</summary>
    public static Vec3 ToGame(Vec3 v, WmoPlacement p)
    {
        const float d2r = MathF.PI / 180f;
        var r = Matrix3.FromEulerAnglesZYX(p.Rot.Y * d2r, p.Rot.X * d2r, p.Rot.Z * d2r);
        var rotated = r.Mul(v);
        var ix = rotated.X + p.Pos.Z; // fixCoords(pos) = (z,x,y)
        var iy = rotated.Y + p.Pos.X;
        var iz = rotated.Z + p.Pos.Y;
        return new Vec3(Mid - ix, Mid - iy, iz);
    }
}
