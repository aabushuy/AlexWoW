using System.Buffers.Binary;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Геометрия WMO (здания/мосты/пещеры): root-файл даёт число групп, group-файлы — вершины (MOVT)
/// и индексы (MOVI). Возвращает треугольники в МОДЕЛЬНОМ пространстве (трансформацию размещения
/// применяет вызывающий). Достаточно для коллизий/LoS.
/// </summary>
public sealed class WmoModel
{
    public List<Vec3> Vertices { get; } = new();
    public List<(int A, int B, int C)> Triangles { get; } = new();

    public static WmoModel? Load(MpqChain mpq, string rootName)
    {
        var root = mpq.ReadFile(rootName);
        if (root is null)
            return null;

        var nGroups = ReadGroupCount(root);
        if (nGroups <= 0)
            return null;

        var model = new WmoModel();
        var baseName = rootName[..^4]; // без ".wmo"
        for (var g = 0; g < nGroups; g++)
        {
            var groupData = mpq.ReadFile($"{baseName}_{g:000}.wmo");
            if (groupData is not null)
                model.AddGroup(groupData);
        }
        return model.Triangles.Count > 0 ? model : null;
    }

    private static int ReadGroupCount(byte[] root)
    {
        var pos = 0;
        while (pos + 8 <= root.Length)
        {
            var magic = ClientData.ReadReversedMagic(root, pos);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(root.AsSpan(pos + 4));
            if (magic == "MOHD")
                return (int)BinaryPrimitives.ReadUInt32LittleEndian(root.AsSpan(pos + 8 + 4)); // nGroups @ +4
            if (size < 0 || pos + 8 + size > root.Length)
                break;
            pos += 8 + size;
        }
        return 0;
    }

    private void AddGroup(byte[] data)
    {
        var verts = new List<Vec3>();
        var indices = new List<int>();

        var pos = 0;
        while (pos + 8 <= data.Length)
        {
            var magic = ClientData.ReadReversedMagic(data, pos);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
            var body = pos + 8;

            if (magic == "MOGP")
            {
                // Заголовок группы = 68 байт, далее субчанки (как в CMaNGOS).
                pos = body + 68;
                continue;
            }
            if (size < 0 || body + size > data.Length)
                break; // битый/обрезанный чанк — дальше не идём
            if (magic == "MOVT")
            {
                for (var i = 0; i + 12 <= size; i += 12)
                    verts.Add(new Vec3(
                        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(body + i)),
                        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(body + i + 4)),
                        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(body + i + 8))));
            }
            else if (magic == "MOVI")
            {
                for (var i = 0; i + 2 <= size; i += 2)
                    indices.Add(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + i)));
            }
            pos = body + size;
        }

        var baseIdx = Vertices.Count;
        Vertices.AddRange(verts);
        for (var i = 0; i + 2 < indices.Count; i += 3)
            Triangles.Add((baseIdx + indices[i], baseIdx + indices[i + 1], baseIdx + indices[i + 2]));
    }
}
