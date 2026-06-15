using System.IO.Compression;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Минимальный кодировщик PNG (RGBA8, без фильтров) без внешних зависимостей — для конвертации
/// иконок BLP клиента в PNG (дев-тул iconmap). IDAT сжимается ZLibStream (zlib-обёртка штатная).
/// </summary>
public static class Png
{
    public static void Write(string path, int width, int height, byte[] rgba)
    {
        using var fs = File.Create(path);
        // Сигнатура PNG.
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        // IHDR: width, height, bitDepth=8, colorType=6 (RGBA), compress=0, filter=0, interlace=0.
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)width);
        WriteBE(ihdr, 4, (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(fs, "IHDR", ihdr);

        // IDAT: каждая строка с фильтром 0 (None), затем zlib-сжатие.
        var raw = new byte[height * (1 + width * 4)];
        var pos = 0;
        for (var y = 0; y < height; y++)
        {
            raw[pos++] = 0; // тип фильтра None
            Array.Copy(rgba, y * width * 4, raw, pos, width * 4);
            pos += width * 4;
        }
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw, 0, raw.Length);
        WriteChunk(fs, "IDAT", ms.ToArray());

        WriteChunk(fs, "IEND", []);
    }

    private static void WriteBE(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBuf = stackalloc byte[4];
        WriteBE(crcBuf, 0, crc);
        s.Write(crcBuf);
    }

    private static void WriteBE(Span<byte> buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
