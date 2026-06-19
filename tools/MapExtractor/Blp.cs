using System.Buffers.Binary;
using System.Text;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Декодер BLP2 (текстуры WoW 3.3.5a) в RGBA8, мип-уровень 0 — для конвертации иконок предметов
/// (Interface\Icons\*.blp) в PNG. Поддержка: палитра (encoding=1, alphaDepth 0/1/4/8),
/// DXT1/3/5 (encoding=2), сырой BGRA (encoding=3). Достаточно для иконок интерфейса.
/// </summary>
public static class Blp
{
    public static (int Width, int Height, byte[] Rgba) Decode(byte[] d)
    {
        if (Encoding.ASCII.GetString(d, 0, 4) != "BLP2")
            throw new InvalidDataException("не BLP2");

        var encoding = d[8];
        var alphaDepth = d[9];
        var alphaEncoding = d[10];
        var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(12));
        var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(16));
        var mip0Offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(20)); // mipOffsets[0]

        var rgba = new byte[width * height * 4];

        switch (encoding)
        {
            case 1: DecodePalette(d, mip0Offset, width, height, alphaDepth, rgba); break;
            case 2: DecodeDxt(d, mip0Offset, width, height, alphaEncoding, rgba); break;
            case 3: DecodeBgra(d, mip0Offset, width, height, rgba); break;
            default: throw new NotSupportedException($"BLP encoding {encoding}");
        }
        return (width, height, rgba);
    }

    // encoding=1: палитра 256×BGRA сразу после заголовка (offset 148), индексы в mip0, альфа отдельно.
    private static void DecodePalette(byte[] d, int off, int w, int h, int alphaDepth, byte[] rgba)
    {
        const int paletteOffset = 20 + 16 * 4 + 16 * 4; // header(20) + mipOffsets(64) + mipSizes(64) = 148
        var n = w * h;
        for (var i = 0; i < n; i++)
        {
            var idx = d[off + i];
            var p = paletteOffset + idx * 4;
            rgba[i * 4 + 0] = d[p + 2]; // R (палитра в BGRA)
            rgba[i * 4 + 1] = d[p + 1]; // G
            rgba[i * 4 + 2] = d[p + 0]; // B
            rgba[i * 4 + 3] = 255;
        }

        var alphaStart = off + n;
        switch (alphaDepth)
        {
            case 8:
                for (var i = 0; i < n; i++)
                    rgba[i * 4 + 3] = d[alphaStart + i];
                break;
            case 4:
                for (var i = 0; i < n; i++)
                {
                    var b = d[alphaStart + i / 2];
                    var a = (i & 1) == 0 ? (b & 0x0F) : (b >> 4);
                    rgba[i * 4 + 3] = (byte)(a * 17); // 0..15 → 0..255
                }
                break;
            case 1:
                for (var i = 0; i < n; i++)
                {
                    var b = d[alphaStart + i / 8];
                    var bit = (b >> (i & 7)) & 1;
                    rgba[i * 4 + 3] = bit != 0 ? (byte)255 : (byte)0;
                }
                break;
                // alphaDepth == 0 → полностью непрозрачный (уже 255)
        }
    }

    // encoding=3: сырой BGRA8 в mip0.
    private static void DecodeBgra(byte[] d, int off, int w, int h, byte[] rgba)
    {
        var n = w * h;
        for (var i = 0; i < n; i++)
        {
            rgba[i * 4 + 0] = d[off + i * 4 + 2]; // R
            rgba[i * 4 + 1] = d[off + i * 4 + 1]; // G
            rgba[i * 4 + 2] = d[off + i * 4 + 0]; // B
            rgba[i * 4 + 3] = d[off + i * 4 + 3]; // A
        }
    }

    // encoding=2: DXT1 (alphaEnc=0), DXT3 (1), DXT5 (7).
    private static void DecodeDxt(byte[] d, int off, int w, int h, int alphaEnc, byte[] rgba)
    {
        var dxt = alphaEnc switch { 0 => 1, 1 => 3, 7 => 5, _ => 1 };
        var blockBytes = dxt == 1 ? 8 : 16;
        var pos = off;
        for (var by = 0; by < h; by += 4)
            for (var bx = 0; bx < w; bx += 4)
            {
                var alpha = new byte[16];
                for (var i = 0; i < 16; i++) alpha[i] = 255;

                if (dxt == 3)
                {
                    for (var i = 0; i < 16; i++)
                    {
                        var b = d[pos + i / 2];
                        var a = (i & 1) == 0 ? (b & 0x0F) : (b >> 4);
                        alpha[i] = (byte)(a * 17);
                    }
                    pos += 8;
                }
                else if (dxt == 5)
                {
                    var a0 = d[pos];
                    var a1 = d[pos + 1];
                    var aBits = 0UL;
                    for (var i = 0; i < 6; i++) aBits |= (ulong)d[pos + 2 + i] << (8 * i);
                    for (var i = 0; i < 16; i++)
                    {
                        var code = (int)((aBits >> (3 * i)) & 0x7);
                        alpha[i] = AlphaValue(a0, a1, code);
                    }
                    pos += 8;
                }

                DecodeColorBlock(d, pos, dxt == 1, out var colors);
                pos += 8;

                for (var py = 0; py < 4; py++)
                    for (var px = 0; px < 4; px++)
                    {
                        var x = bx + px;
                        var y = by + py;
                        if (x >= w || y >= h) continue;
                        var local = py * 4 + px;
                        var (r, g, b, ca) = colors[local];
                        var di = (y * w + x) * 4;
                        rgba[di + 0] = r;
                        rgba[di + 1] = g;
                        rgba[di + 2] = b;
                        // DXT1 может нести 1-битную альфу (ca=0 → прозрачно); иначе берём из DXT3/5.
                        rgba[di + 3] = dxt == 1 ? ca : alpha[local];
                    }
            }
    }

    private static byte AlphaValue(byte a0, byte a1, int code)
    {
        if (code == 0) return a0;
        if (code == 1) return a1;
        if (a0 > a1)
            return (byte)(((8 - code) * a0 + (code - 1) * a1) / 7);
        return code switch
        {
            6 => 0,
            7 => 255,
            _ => (byte)(((6 - code) * a0 + (code - 1) * a1) / 5),
        };
    }

    private static void DecodeColorBlock(byte[] d, int pos, bool dxt1, out (byte R, byte G, byte B, byte A)[] colors)
    {
        var c0 = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(pos));
        var c1 = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(pos + 2));
        var (r0, g0, b0) = Rgb565(c0);
        var (r1, g1, b1) = Rgb565(c1);

        var pal = new (byte R, byte G, byte B, byte A)[4];
        pal[0] = (r0, g0, b0, 255);
        pal[1] = (r1, g1, b1, 255);
        if (!dxt1 || c0 > c1)
        {
            pal[2] = ((byte)((2 * r0 + r1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * b0 + b1) / 3), 255);
            pal[3] = ((byte)((r0 + 2 * r1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((b0 + 2 * b1) / 3), 255);
        }
        else
        {
            pal[2] = ((byte)((r0 + r1) / 2), (byte)((g0 + g1) / 2), (byte)((b0 + b1) / 2), 255);
            pal[3] = (0, 0, 0, 0); // прозрачный (1-битная альфа DXT1)
        }

        colors = new (byte, byte, byte, byte)[16];
        var bits = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(pos + 4));
        for (var i = 0; i < 16; i++)
            colors[i] = pal[(bits >> (2 * i)) & 0x3];
    }

    private static (byte R, byte G, byte B) Rgb565(ushort c)
    {
        var r = (c >> 11) & 0x1F;
        var g = (c >> 5) & 0x3F;
        var b = c & 0x1F;
        return ((byte)(r * 255 / 31), (byte)(g * 255 / 63), (byte)(b * 255 / 31));
    }
}
