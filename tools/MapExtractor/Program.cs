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

// Стартовая экипировка: charstartoutfit <dataDir> <out.sql> — CharStartOutfit.dbc → SQL для playercreateinfo_item.
if (args.Length >= 3 && args[0].Equals("charstartoutfit", StringComparison.OrdinalIgnoreCase))
{
    using var mpqC = new MpqChain(args[1]);
    CharStartOutfit.ExtractToSql(mpqC, args[2]);
    return;
}

// Реакции фракций: factiontemplate <dataDir> <out.sql> — FactionTemplate.dbc → SQL для авто-агро (M6.7).
if (args.Length >= 3 && args[0].Equals("factiontemplate", StringComparison.OrdinalIgnoreCase))
{
    using var mpqF = new MpqChain(args[1]);
    FactionTemplateExtract.ExtractToSql(mpqF, args[2]);
    return;
}

// Дамп SpellCastTimes.dbc: spellcasttimes <dataDir> — index → base cast time (мс) как C#-инициализатор (M10.2).
if (args.Length >= 2 && args[0].Equals("spellcasttimes", StringComparison.OrdinalIgnoreCase))
{
    using var mpqSct = new MpqChain(args[1]);
    SpellCastTimesDump.Print(mpqSct);
    return;
}

// Дамп SpellDuration.dbc: spelldurations <dataDir> — index → base duration (мс) как C#-инициализатор (M10.4b).
if (args.Length >= 2 && args[0].Equals("spelldurations", StringComparison.OrdinalIgnoreCase))
{
    using var mpqSd = new MpqChain(args[1]);
    SpellDurationsDump.Print(mpqSd);
    return;
}

// Таланты: talent <dataDir> <out.sql> — Talent.dbc + TalentTab.dbc → SQL для talent/talent_tab (M9.6/M9.7).
if (args.Length >= 3 && args[0].Equals("talent", StringComparison.OrdinalIgnoreCase))
{
    using var mpqT = new MpqChain(args[1]);
    TalentDump.ExtractToSql(mpqT, args[2]);
    return;
}

// Иконки предметов: iconmap <dataDir> <usedIds.txt> <iconsOutDir> <map.tsv> —
//   ItemDisplayInfo.dbc + Interface\Icons\*.blp → PNG + карта displayid→иконка (админка Web, полный офлайн).
if (args.Length >= 5 && args[0].Equals("iconmap", StringComparison.OrdinalIgnoreCase))
{
    using var mpqIm = new MpqChain(args[1]);
    IconMapDump.Extract(mpqIm, args[2], args[3], args[4]);
    return;
}

// Иконки спеллов: spell-iconmap <dataDir> <iconsOutDir> <map.tsv> —
//   SpellIcon.dbc → PNG (новые в общую папку с item-иконками) + карта SpellIconID→иконка
//   для preview-блока на /Ticket. Фильтр used-ids не нужен: иконок спеллов всего ~5k.
if (args.Length >= 4 && args[0].Equals("spell-iconmap", StringComparison.OrdinalIgnoreCase))
{
    using var mpqSi = new MpqChain(args[1]);
    SpellIconMapDump.Extract(mpqSi, args[2], args[3]);
    return;
}

// Боевые рейтинги: combatratings <dataDir> <out.json> — gtChanceToMeleeCrit(+Base).dbc → JSON (защитные статы).
if (args.Length >= 3 && args[0].Equals("combatratings", StringComparison.OrdinalIgnoreCase))
{
    using var mpqCr = new MpqChain(args[1]);
    CombatRatingDump.ExtractToJson(mpqCr, args[2]);
    return;
}

// Диагностика Faction.dbc: faction <dataDir> <id> [id2 ...] — печать reputationListID и base standing.
if (args.Length >= 3 && args[0].Equals("faction", StringComparison.OrdinalIgnoreCase))
{
    using var mpqFd = new MpqChain(args[1]);
    var fids = args.Skip(2).Select(uint.Parse).ToArray();
    FactionDump.Print(mpqFd, fids);
    FactionDump.PrintTemplates(mpqFd, fids);
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

// Проверка LoS: vmaplos <vmapDir> <map> x1 y1 z1 x2 y2 z2 — через серверный загрузчик DataStores.
if (args.Length >= 9 && args[0].Equals("vmaplos", StringComparison.OrdinalIgnoreCase))
{
    var vm = new AlexWoW.DataStores.Collision.Vmaps(args[1]);
    float P(int i) => float.Parse(args[i], System.Globalization.CultureInfo.InvariantCulture);
    var losMap = uint.Parse(args[2]);
    var los = vm.IsInLineOfSight(losMap, P(3), P(4), P(5), P(6), P(7), P(8));
    Console.WriteLine($"Available={vm.Available}  LoS = {los} (true=видно, false=стена)");
    return;
}

// Сборка vmap: vmap <dataDir> <outDir> [mapId] — пер-тайл коллизии WMO в игровых координатах.
if (args.Length >= 2 && args[0].Equals("vmap", StringComparison.OrdinalIgnoreCase))
{
    var dataDir2 = args[1];
    var outDir2 = args.Length > 2 ? args[2] : Path.Combine(Environment.CurrentDirectory, "vmaps");
    int? onlyMap2 = args.Length > 3 && uint.TryParse(args[3], out var mm) ? (int)mm : null;
    Directory.CreateDirectory(outDir2);
    using var mpq2 = new MpqChain(dataDir2);
    var maps2 = ClientData.ReadMaps(mpq2);
    var sw2 = System.Diagnostics.Stopwatch.StartNew();
    long totalTiles = 0;

    foreach (var map in maps2)
    {
        if (onlyMap2 is { } only2 && map.Id != only2) continue;
        var adtTiles = ClientData.ReadExistingTiles(mpq2, map.Directory);
        if (adtTiles.Count == 0) continue;

        var seen = new HashSet<uint>();
        var tileTris = new Dictionary<(int, int), List<float>>();

        foreach (var (ax, ay) in adtTiles)
        {
            var adt = mpq2.ReadFile($"World\\Maps\\{map.Directory}\\{map.Directory}_{ax}_{ay}.adt");
            if (adt is null) continue;
            foreach (var p in VmapExtract.ReadWmoPlacements(adt))
            {
                if ((p.Flags & 0x1) != 0) continue;      // разрушаемые — в динамику, пропускаем
                if (!seen.Add(p.UniqueId)) continue;       // инстанс уже обработан в соседнем ADT
                WmoModel? wmo;
                try { wmo = WmoModel.Load(mpq2, p.Name); } catch { continue; }
                if (wmo is null) continue;
                foreach (var (ia, ib, ic) in wmo.Triangles)
                {
                    var g0 = VmapExtract.ToGame(wmo.Vertices[ia], p);
                    var g1 = VmapExtract.ToGame(wmo.Vertices[ib], p);
                    var g2 = VmapExtract.ToGame(wmo.Vertices[ic], p);
                    var gxHi = VmapWriter.Grid(MathF.Min(g0.X, MathF.Min(g1.X, g2.X)));
                    var gxLo = VmapWriter.Grid(MathF.Max(g0.X, MathF.Max(g1.X, g2.X)));
                    var gyHi = VmapWriter.Grid(MathF.Min(g0.Y, MathF.Min(g1.Y, g2.Y)));
                    var gyLo = VmapWriter.Grid(MathF.Max(g0.Y, MathF.Max(g1.Y, g2.Y)));
                    for (var gx = gxLo; gx <= gxHi; gx++)
                    for (var gy = gyLo; gy <= gyHi; gy++)
                    {
                        if (gx is < 0 or > 63 || gy is < 0 or > 63) continue;
                        if (!tileTris.TryGetValue((gx, gy), out var list))
                            tileTris[(gx, gy)] = list = new List<float>();
                        list.AddRange(new[] { g0.X, g0.Y, g0.Z, g1.X, g1.Y, g1.Z, g2.X, g2.Y, g2.Z });
                    }
                }
            }
        }

        foreach (var ((gx, gy), tris) in tileTris)
        {
            VmapWriter.Write(Path.Combine(outDir2, $"{map.Id:D3}{gx:D2}{gy:D2}.vmap"), tris);
            totalTiles++;
        }
        if (tileTris.Count > 0)
            Console.WriteLine($"  map {map.Id,4} {map.Directory,-26} vmap-тайлов: {tileTris.Count}");
    }
    sw2.Stop();
    Console.WriteLine($"\nГотово: {totalTiles} vmap-тайлов за {sw2.Elapsed.TotalSeconds:F1} c → {outDir2}");
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
