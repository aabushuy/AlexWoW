using System.Buffers.Binary;
using System.Text;

namespace AlexWoW.MapExtractor;

/// <summary>Запись файла рельефа в формате CMaNGOS (.map, magic MAPS/v1.4): только высота (float).</summary>
public static class MapWriter
{
    private const uint Build = 12340;

    public static void Write(string path, AdtHeight h)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var v in h.V9) { if (v < min) min = v; if (v > max) max = v; }
        foreach (var v in h.V8) { if (v < min) min = v; if (v > max) max = v; }

        const int fileHeaderSize = 11 * 4; // GridMapFileHeader
        const int heightHeaderSize = 4 + 4 + 4 + 4; // MHGT + flags + gridHeight + gridMaxHeight
        var heightSize = heightHeaderSize + h.V9.Length * 4 + h.V8.Length * 4;

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        // GridMapFileHeader
        w.Write(Encoding.ASCII.GetBytes("MAPS"));
        w.Write(Encoding.ASCII.GetBytes("v1.4"));
        w.Write(Build);
        w.Write(0u);                 // areaMapOffset
        w.Write(0u);                 // areaMapSize
        w.Write((uint)fileHeaderSize); // heightMapOffset
        w.Write((uint)heightSize);   // heightMapSize
        w.Write(0u);                 // liquidMapOffset
        w.Write(0u);                 // liquidMapSize
        w.Write(0u);                 // holesOffset
        w.Write(0u);                 // holesSize

        // GridMapHeightHeader (flags=0 → float)
        w.Write(Encoding.ASCII.GetBytes("MHGT"));
        w.Write(0u);                 // flags
        w.Write(min);                // gridHeight
        w.Write(max);                // gridMaxHeight

        Span<byte> tmp = stackalloc byte[4];
        foreach (var v in h.V9) { BinaryPrimitives.WriteSingleLittleEndian(tmp, v); w.Write(tmp); }
        foreach (var v in h.V8) { BinaryPrimitives.WriteSingleLittleEndian(tmp, v); w.Write(tmp); }
    }
}
