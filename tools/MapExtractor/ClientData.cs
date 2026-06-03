using System.Buffers.Binary;
using System.Text;

namespace AlexWoW.MapExtractor;

public readonly record struct MapEntry(uint Id, string Directory);

/// <summary>Чтение клиентских данных: Map.dbc (список карт) и WDT (какие ADT-тайлы есть).</summary>
public static class ClientData
{
    /// <summary>Map.dbc (WDBC): запись = id(u32)@0 + directory(string-offset u32)@4.</summary>
    public static List<MapEntry> ReadMaps(MpqChain mpq)
    {
        var data = mpq.ReadFile("DBFilesClient\\Map.dbc")
            ?? throw new FileNotFoundException("Map.dbc не найден в MPQ");

        if (Encoding.ASCII.GetString(data, 0, 4) != "WDBC")
            throw new InvalidDataException("Map.dbc: не WDBC");

        var recordCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var recordSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        var stringSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16));
        var recordsOffset = 20;
        var stringsOffset = recordsOffset + (int)(recordCount * recordSize);

        string ReadStr(uint off)
        {
            var p = stringsOffset + (int)off;
            var end = p;
            while (end < data.Length && data[end] != 0) end++;
            return Encoding.UTF8.GetString(data, p, end - p);
        }

        var result = new List<MapEntry>((int)recordCount);
        for (var i = 0; i < recordCount; i++)
        {
            var rec = recordsOffset + i * (int)recordSize;
            var id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rec));
            var dirOff = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rec + 4));
            result.Add(new MapEntry(id, ReadStr(dirOff)));
        }
        return result;
    }

    /// <summary>WDT → список существующих ADT-тайлов (x,y). MAIN: 64×64 SMAreaInfo (flags|asyncId), бит0 = есть ADT.</summary>
    public static List<(int X, int Y)> ReadExistingTiles(MpqChain mpq, string dir)
    {
        var name = $"World\\Maps\\{dir}\\{dir}.wdt";
        var data = mpq.ReadFile(name);
        var tiles = new List<(int, int)>();
        if (data is null)
            return tiles;

        // Идём по чанкам; magic хранится реверснутым ('MAIN' → 'NIAM').
        var pos = 0;
        while (pos + 8 <= data.Length)
        {
            var magic = ReadReversedMagic(data, pos);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
            var body = pos + 8;
            if (magic == "MAIN")
            {
                for (var y = 0; y < 64; y++)
                for (var x = 0; x < 64; x++)
                {
                    var e = body + (y * 64 + x) * 8;
                    if (e + 4 > data.Length) continue;
                    var flags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(e));
                    if ((flags & 0x1) != 0)
                        tiles.Add((x, y));
                }
                break;
            }
            pos = body + size;
        }
        return tiles;
    }

    public static string ReadReversedMagic(byte[] data, int pos)
    {
        Span<char> c = stackalloc char[4];
        c[0] = (char)data[pos + 3];
        c[1] = (char)data[pos + 2];
        c[2] = (char)data[pos + 1];
        c[3] = (char)data[pos + 0];
        return new string(c);
    }
}
