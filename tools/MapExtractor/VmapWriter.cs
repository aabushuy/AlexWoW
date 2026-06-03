using System.Buffers.Binary;
using System.Text;

namespace AlexWoW.MapExtractor;

/// <summary>Запись коллизий тайла (.vmap): magic "AVMP" + u32 version + u32 triCount + 9 float/треуг.</summary>
public static class VmapWriter
{
    private const float SizeOfGrids = 1600.0f / 3.0f;

    public static int Grid(float c) => (int)(32 - c / SizeOfGrids);

    public static void Write(string path, List<float> tris)
    {
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write(Encoding.ASCII.GetBytes("AVMP"));
        w.Write(1u);
        w.Write((uint)(tris.Count / 9));
        Span<byte> tmp = stackalloc byte[4];
        foreach (var f in tris)
        {
            BinaryPrimitives.WriteSingleLittleEndian(tmp, f);
            w.Write(tmp);
        }
    }
}
