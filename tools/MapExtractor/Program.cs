using System.Diagnostics;
using AlexWoW.MapExtractor;

// Режим проверки: verify <mapsDir> <mapId> <x> <y> — печатает высоту земли загрузчиком DataStores.
if (args.Length >= 5 && args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
{
    var terrain = new AlexWoW.DataStores.Terrain.TerrainMaps(args[1]);
    var mid = uint.Parse(args[2]);
    var px = float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
    var py = float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
    var ground = terrain.GetHeight(mid, px, py);
    Console.WriteLine($"Available={terrain.Available}  height(map={mid}, {px}, {py}) = {(ground?.ToString() ?? "null")}");
    return;
}

// Проверка vmap: vmapverify <dataDir> <mapDir> <adtX> <adtY> — список WMO в тайле + AABB (игровые коорд.).
if (args.Length >= 5 && args[0].Equals("vmapverify", StringComparison.OrdinalIgnoreCase))
{
    using var mpqV = new MpqChain(args[1]);
    var dir = args[2];
    var ax = int.Parse(args[3]);
    var ay = int.Parse(args[4]);
    var adt = mpqV.ReadFile($"World\\Maps\\{dir}\\{dir}_{ax}_{ay}.adt")
        ?? throw new FileNotFoundException("ADT не найден");
    var placements = VmapExtract.ReadWmoPlacements(adt);
    Console.WriteLine($"WMO-инстансов в {dir}_{ax}_{ay}: {placements.Count}");
    foreach (var p in placements)
    {
        if ((p.Flags & 0x1) != 0) { Console.WriteLine($"  [skip destructible] {p.Name}"); continue; }
        var wmo = WmoModel.Load(mpqV, p.Name);
        if (wmo is null) { Console.WriteLine($"  [no geom] {p.Name}"); continue; }
        float minx = float.MaxValue, miny = float.MaxValue, minz = float.MaxValue;
        float maxx = float.MinValue, maxy = float.MinValue, maxz = float.MinValue;
        foreach (var v in wmo.Vertices)
        {
            var g = VmapExtract.ToGame(v, p);
            minx = MathF.Min(minx, g.X); maxx = MathF.Max(maxx, g.X);
            miny = MathF.Min(miny, g.Y); maxy = MathF.Max(maxy, g.Y);
            minz = MathF.Min(minz, g.Z); maxz = MathF.Max(maxz, g.Z);
        }
        Console.WriteLine($"  {System.IO.Path.GetFileName(p.Name),-34} tris={wmo.Triangles.Count,6}  " +
            $"AABB X[{minx:F0}..{maxx:F0}] Y[{miny:F0}..{maxy:F0}] Z[{minz:F0}..{maxz:F0}]");
    }
    return;
}

// Использование: MapExtractor <dataDir> <outDir> [mapId]
//   dataDir — каталог Data клиента 3.3.5a; outDir — куда писать maps/*.map; mapId — только эта карта.
var dataDir = args.Length > 0 ? args[0] : @"D:\Games\WoW335\Data";
var outDir = args.Length > 1 ? args[1] : Path.Combine(Environment.CurrentDirectory, "maps");
int? onlyMap = args.Length > 2 && uint.TryParse(args[2], out var m) ? (int)m : null;

Directory.CreateDirectory(outDir);
Console.WriteLine($"Data: {dataDir}\nOut:  {outDir}\nФильтр карты: {(onlyMap?.ToString() ?? "все")}");

using var mpq = new MpqChain(dataDir);
var maps = ClientData.ReadMaps(mpq);
Console.WriteLine($"Карт в Map.dbc: {maps.Count}");

var sw = Stopwatch.StartNew();
long tilesWritten = 0, mapsWithTiles = 0;

foreach (var map in maps)
{
    if (onlyMap is { } only && map.Id != only)
        continue;

    var tiles = ClientData.ReadExistingTiles(mpq, map.Directory);
    if (tiles.Count == 0)
        continue;

    var written = 0;
    foreach (var (x, y) in tiles)
    {
        var adtName = $"World\\Maps\\{map.Directory}\\{map.Directory}_{x}_{y}.adt";
        var adt = mpq.ReadFile(adtName);
        if (adt is null)
            continue;

        AdtHeight? h;
        try { h = AdtHeight.Parse(adt); }
        catch (Exception ex) { Console.WriteLine($"  ! {adtName}: {ex.Message}"); continue; }
        if (h is null)
            continue;

        // .map: имя {mapId:D3}{gx:D2}{gy:D2}, где gx=y, gy=x (см. якорь Azeroth_32_48 → 0004832).
        var outFile = Path.Combine(outDir, $"{map.Id:D3}{y:D2}{x:D2}.map");
        MapWriter.Write(outFile, h);
        written++;
    }

    if (written > 0)
    {
        mapsWithTiles++;
        tilesWritten += written;
        Console.WriteLine($"  map {map.Id,4} {map.Directory,-28} тайлов: {written}");
    }
}

sw.Stop();
Console.WriteLine($"\nГотово: {tilesWritten} тайлов из {mapsWithTiles} карт за {sw.Elapsed.TotalSeconds:F1} c → {outDir}");
