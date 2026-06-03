using System.Buffers.Binary;
using AlexWoW.DataStores.Navigation;
using AlexWoW.DataStores.Terrain;
using AlexWoW.MmapGen;
using DotRecast.Core;
using DotRecast.Detour.Io;

// mmap <mapsDir> <vmapsDir> <outDir> [mapId] [gx gy] — навмеш-тайлы (рельеф + vmap-стены).
if (args.Length >= 4 && args[0].Equals("mmap", StringComparison.OrdinalIgnoreCase))
{
    var mapsDir = args[1];
    var vmapsDir = args[2];
    var outDir = args[3];
    int? onlyMap = args.Length > 4 && uint.TryParse(args[4], out var mm) ? (int)mm : null;
    int? onlyGx = args.Length > 6 && int.TryParse(args[5], out var ogx) ? ogx : null;
    int? onlyGy = args.Length > 6 && int.TryParse(args[6], out var ogy) ? ogy : null;
    Directory.CreateDirectory(outDir);

    var terrain = new TerrainMaps(mapsDir);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    int built = 0, failed = 0;

    foreach (var mapFile in Directory.GetFiles(mapsDir, "*.map"))
    {
        var name = Path.GetFileNameWithoutExtension(mapFile); // MMMGGgg
        if (name.Length != 7) continue;
        var mapId = int.Parse(name.Substring(0, 3));
        var gx = int.Parse(name.Substring(3, 2));
        var gy = int.Parse(name.Substring(5, 2));
        if (onlyMap is { } om && mapId != om) continue;
        if (onlyGx is { } ox && gx != ox) continue;
        if (onlyGy is { } oy && gy != oy) continue;

        var vmapPath = Path.Combine(vmapsDir, $"{name}.vmap");
        var vmapTris = File.Exists(vmapPath) ? VmapRead.ReadTriangles(vmapPath) : null;

        var data = NavmeshBuild.BuildTile(terrain, vmapTris, (uint)mapId, gx, gy);
        if (data is null) { failed++; continue; }

        using (var fs = File.Create(Path.Combine(outDir, $"{name}.mmtile")))
        using (var bw = new BinaryWriter(fs))
            new DtMeshDataWriter().Write(bw, data, RcByteOrder.LITTLE_ENDIAN, false);
        built++;
        if (built % 100 == 0) Console.WriteLine($"  …{built} тайлов");
    }
    sw.Stop();
    Console.WriteLine($"Готово: {built} навмеш-тайлов ({failed} без навмеша) за {sw.Elapsed.TotalSeconds:F1} c → {outDir}");
    return;
}

// mmpath <mmapsDir> <map> sx sy sz ex ey ez — путь через серверный навмеш.
if (args.Length >= 9 && args[0].Equals("mmpath", StringComparison.OrdinalIgnoreCase))
{
    var nav = new Navmesh(args[1]);
    float P(int i) => float.Parse(args[i], System.Globalization.CultureInfo.InvariantCulture);
    var path = nav.FindPath(uint.Parse(args[2]), P(3), P(4), P(5), P(6), P(7), P(8));
    Console.WriteLine($"Available={nav.Available}");
    if (path is null) { Console.WriteLine("Путь не найден / нет навмеша"); return; }
    Console.WriteLine($"Путь из {path.Count} точек:");
    foreach (var (x, y, z) in path)
        Console.WriteLine($"  ({x:F1}, {y:F1}, {z:F1})");
    return;
}

Console.WriteLine("Использование:\n  mmap <mapsDir> <vmapsDir> <outDir> [mapId] [gx gy]\n  mmpath <mmapsDir> <map> sx sy sz ex ey ez");
