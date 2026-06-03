namespace AlexWoW.DataStores.Terrain;

/// <summary>
/// Один файл рельефа <c>maps/MMMGGgg.map</c> (формат CMaNGOS, magic «MAPS»/«v1.4»).
/// Загружает только высоту (area/liquid/holes пропускаем). Сетки высот: V9 129×129 (углы)
/// и V8 128×128 (центры ячеек). int16/int8-варианты разворачиваем в float при загрузке.
/// </summary>
public sealed class GridMap
{
    public const float SizeOfGrids = 1600.0f / 3.0f; // 533.333…
    public const int MapResolution = 128;
    public const float InvalidHeight = -100000.0f;

    private const uint MagicMaps = 0x5350414D;    // 'M','A','P','S'
    private const uint MagicVersion = 0x342E3176; // 'v','1','.','4'
    private const uint MagicHeight = 0x5447484D;  // 'M','H','G','T'

    private const uint HeightNoHeight = 0x0001;
    private const uint HeightAsInt16 = 0x0002;
    private const uint HeightAsInt8 = 0x0004;

    private float _flatHeight = InvalidHeight;
    private float[]? _v9; // 129*129
    private float[]? _v8; // 128*128

    private GridMap() { }

    /// <summary>Грузит .map; null — если файл отсутствует/повреждён/неподходящий формат.</summary>
    public static GridMap? Load(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);

            var mapMagic = r.ReadUInt32();
            var versionMagic = r.ReadUInt32();
            r.ReadUInt32(); // buildMagic — не проверяем
            if (mapMagic != MagicMaps || versionMagic != MagicVersion)
                return null;

            r.ReadUInt32(); // areaMapOffset
            r.ReadUInt32(); // areaMapSize
            var heightMapOffset = r.ReadUInt32();
            r.ReadUInt32(); // heightMapSize
            // liquidMapOffset/Size, holesOffset/Size — пропускаем

            var map = new GridMap();
            if (heightMapOffset != 0)
            {
                fs.Seek(heightMapOffset, SeekOrigin.Begin);
                map.ReadHeight(r);
            }
            return map;
        }
        catch
        {
            return null;
        }
    }

    private void ReadHeight(BinaryReader r)
    {
        var fourcc = r.ReadUInt32();
        if (fourcc != MagicHeight)
            return;
        var flags = r.ReadUInt32();
        var gridHeight = r.ReadSingle();
        var gridMaxHeight = r.ReadSingle();

        _flatHeight = gridHeight;

        if ((flags & HeightNoHeight) != 0)
            return; // плоско

        _v9 = new float[129 * 129];
        _v8 = new float[128 * 128];

        if ((flags & HeightAsInt16) != 0)
        {
            var mult = (gridMaxHeight - gridHeight) / 65535f;
            for (var i = 0; i < _v9.Length; i++) _v9[i] = r.ReadUInt16() * mult + gridHeight;
            for (var i = 0; i < _v8.Length; i++) _v8[i] = r.ReadUInt16() * mult + gridHeight;
        }
        else if ((flags & HeightAsInt8) != 0)
        {
            var mult = (gridMaxHeight - gridHeight) / 255f;
            for (var i = 0; i < _v9.Length; i++) _v9[i] = r.ReadByte() * mult + gridHeight;
            for (var i = 0; i < _v8.Length; i++) _v8[i] = r.ReadByte() * mult + gridHeight;
        }
        else
        {
            for (var i = 0; i < _v9.Length; i++) _v9[i] = r.ReadSingle();
            for (var i = 0; i < _v8.Length; i++) _v8[i] = r.ReadSingle();
        }
    }

    /// <summary>Высота земли в мировых координатах (x,y). InvalidHeight, если нет данных.</summary>
    public float GetHeight(float x, float y)
    {
        if (_v9 is null || _v8 is null)
            return _flatHeight;

        x = MapResolution * (32 - x / SizeOfGrids);
        y = MapResolution * (32 - y / SizeOfGrids);

        var xInt = (int)x;
        var yInt = (int)y;
        x -= xInt;
        y -= yInt;
        xInt &= MapResolution - 1;
        yInt &= MapResolution - 1;

        // Билинейная интерполяция по одному из 4 треугольников ячейки (как в CMaNGOS GridMap).
        float a, b, c;
        if (x + y < 1)
        {
            if (x > y)
            {
                var h1 = _v9[xInt * 129 + yInt];
                var h2 = _v9[(xInt + 1) * 129 + yInt];
                var h5 = 2 * _v8[xInt * 128 + yInt];
                a = h2 - h1; b = h5 - h1 - h2; c = h1;
            }
            else
            {
                var h1 = _v9[xInt * 129 + yInt];
                var h3 = _v9[xInt * 129 + yInt + 1];
                var h5 = 2 * _v8[xInt * 128 + yInt];
                a = h5 - h1 - h3; b = h3 - h1; c = h1;
            }
        }
        else
        {
            if (x > y)
            {
                var h2 = _v9[(xInt + 1) * 129 + yInt];
                var h4 = _v9[(xInt + 1) * 129 + yInt + 1];
                var h5 = 2 * _v8[xInt * 128 + yInt];
                a = h2 + h4 - h5; b = h4 - h2; c = h5 - h4;
            }
            else
            {
                var h3 = _v9[xInt * 129 + yInt + 1];
                var h4 = _v9[(xInt + 1) * 129 + yInt + 1];
                var h5 = 2 * _v8[xInt * 128 + yInt];
                a = h4 - h3; b = h3 + h4 - h5; c = h5 - h4;
            }
        }
        return a * x + b * y + c;
    }
}
