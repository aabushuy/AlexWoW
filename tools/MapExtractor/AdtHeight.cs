using System.Buffers.Binary;

namespace AlexWoW.MapExtractor;

/// <summary>Высотные сетки одного ADT-тайла: V9 129×129 (углы) + V8 128×128 (центры).</summary>
public sealed class AdtHeight
{
    public float[] V9 { get; } = new float[129 * 129];
    public float[] V8 { get; } = new float[128 * 128];

    private const int CellSize = 8;        // ADT_CELL_SIZE
    private const int McnkHeaderSize = 0x80;

    // Смещения в 128-байтном заголовке MCNK.
    private const int OffIndexX = 0x04;
    private const int OffIndexY = 0x08;
    private const int OffYpos = 0x70;      // база высоты (третий float позиции)

    /// <summary>Парсит ADT, заполняет V9/V8. Возвращает null, если нет ни одного MCNK.</summary>
    public static AdtHeight? Parse(byte[] adt)
    {
        var h = new AdtHeight();
        var found = false;

        var pos = 0;
        while (pos + 8 <= adt.Length)
        {
            var magic = ClientData.ReadReversedMagic(adt, pos);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(adt.AsSpan(pos + 4));
            var body = pos + 8;

            if (magic == "MCNK")
            {
                h.ReadCell(adt, body, size);
                found = true;
            }
            pos = body + size;
        }
        return found ? h : null;
    }

    private void ReadCell(byte[] adt, int header, int chunkSize)
    {
        var indexX = (int)BinaryPrimitives.ReadUInt32LittleEndian(adt.AsSpan(header + OffIndexX));
        var indexY = (int)BinaryPrimitives.ReadUInt32LittleEndian(adt.AsSpan(header + OffIndexY));
        var ypos = BinaryPrimitives.ReadSingleLittleEndian(adt.AsSpan(header + OffYpos));
        if (indexX is < 0 or > 15 || indexY is < 0 or > 15)
            return;

        // Ищем субчанк MCVT внутри MCNK (после 128-байтного заголовка). Надёжнее, чем ofsMCVT.
        var floats = -1;
        var p = header + McnkHeaderSize;
        var end = header + chunkSize;
        while (p + 8 <= end)
        {
            var m = ClientData.ReadReversedMagic(adt, p);
            var sz = (int)BinaryPrimitives.ReadUInt32LittleEndian(adt.AsSpan(p + 4));
            if (m == "MCVT") { floats = p + 8; break; }
            p += 8 + sz;
        }
        if (floats < 0)
            return;

        float H(int idx) => BinaryPrimitives.ReadSingleLittleEndian(adt.AsSpan(floats + idx * 4));

        var i = indexY; // строка → ось worldX (как в экстракторе/загрузчике CMaNGOS)
        var j = indexX; // столбец → ось worldY

        // V9 (углы): 9×9
        for (var y = 0; y <= CellSize; y++)
        {
            var cy = i * CellSize + y;
            for (var x = 0; x <= CellSize; x++)
            {
                var cx = j * CellSize + x;
                V9[cy * 129 + cx] = ypos + H(y * (CellSize * 2 + 1) + x);
            }
        }
        // V8 (центры): 8×8
        for (var y = 0; y < CellSize; y++)
        {
            var cy = i * CellSize + y;
            for (var x = 0; x < CellSize; x++)
            {
                var cx = j * CellSize + x;
                V8[cy * 128 + cx] = ypos + H(y * (CellSize * 2 + 1) + CellSize + 1 + x);
            }
        }
    }
}
