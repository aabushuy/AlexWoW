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
