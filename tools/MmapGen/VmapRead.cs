using System.Buffers.Binary;

namespace AlexWoW.MmapGen;

/// <summary>Чтение .vmap (AVMP): magic + version + count + 9 float/треугольник → плоский float[].</summary>
public static class VmapRead
{
    private const uint Magic = 0x504D5641; // 'A','V','M','P'

    public static float[]? ReadTriangles(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic)
            return null;
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
        var tris = new float[count * 9];
        var src = bytes.AsSpan(12);
        for (var i = 0; i < tris.Length; i++)
            tris[i] = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i * 4));
        return tris;
    }
}
