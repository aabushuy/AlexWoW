using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Извлечение иконок предметов из клиента 3.3.5a (полный офлайн) для админки AlexWoW.Web.
/// ItemDisplayInfo.dbc даёт displayid → имя иконки (поле 5, InventoryIcon[0]); BLP-иконки
/// (Interface\Icons\*.blp) конвертируются в PNG. Ограничиваем набором displayid, реально
/// используемых в item_template (файл со списком id — из БД).
/// Выход: PNG в wwwroot/icons/, TSV-карта displayid→иконка.
/// </summary>
public static class IconMapDump
{
    public static void Extract(MpqChain mpq, string usedIdsPath, string iconsOutDir, string mapOutPath)
    {
        var data = mpq.ReadFile("DBFilesClient\\ItemDisplayInfo.dbc")
            ?? throw new FileNotFoundException("ItemDisplayInfo.dbc не найден в MPQ");
        if (Encoding.ASCII.GetString(data, 0, 4) != "WDBC")
            throw new InvalidDataException("ItemDisplayInfo.dbc: не WDBC");

        var recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        const int recordsOffset = 20;
        var stringStart = recordsOffset + recordCount * recordSize;
        Console.WriteLine($"ItemDisplayInfo.dbc: записей={recordCount}, размер записи={recordSize}");

        uint U(int rec, int field) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rec + field * 4));
        string Str(int rec, int field)
        {
            var off = (int)U(rec, field);
            if (off == 0) return string.Empty;
            var p = stringStart + off;
            var end = p;
            while (end < data.Length && data[end] != 0) end++;
            return Encoding.ASCII.GetString(data, p, end - p);
        }

        // displayid → имя иконки (поле 5 = InventoryIcon[0]).
        var all = new Dictionary<uint, string>();
        for (var i = 0; i < recordCount; i++)
        {
            var rec = recordsOffset + i * recordSize;
            var id = U(rec, 0);
            if (id == 0) continue;
            var icon = Str(rec, 5).Trim();
            if (icon.Length > 0)
                all[id] = icon;
        }
        Console.WriteLine($"displayid с иконкой: {all.Count}");

        // Ограничиваем используемыми в item_template displayid (если задан файл).
        HashSet<uint>? used = null;
        if (!string.IsNullOrEmpty(usedIdsPath) && File.Exists(usedIdsPath))
        {
            used = [.. File.ReadLines(usedIdsPath)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && uint.TryParse(l, out _))
                .Select(l => uint.Parse(l, CultureInfo.InvariantCulture))];
            Console.WriteLine($"used displayid из {usedIdsPath}: {used.Count}");
        }

        var map = used is null
            ? all
            : all.Where(kv => used.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        // Конвертируем уникальные иконки BLP → PNG.
        Directory.CreateDirectory(iconsOutDir);
        var icons = map.Values.Select(v => v.ToLowerInvariant()).Distinct().ToList();
        Console.WriteLine($"уникальных иконок к извлечению: {icons.Count}");

        int ok = 0, miss = 0, fail = 0;
        foreach (var icon in icons)
        {
            var blp = mpq.ReadFile($"Interface\\Icons\\{icon}.blp");
            if (blp is null) { miss++; continue; }
            try
            {
                var (w, h, rgba) = Blp.Decode(blp);
                Png.Write(Path.Combine(iconsOutDir, icon + ".png"), w, h, rgba);
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                if (fail <= 10) Console.WriteLine($"  ! {icon}: {ex.Message}");
            }
        }
        Console.WriteLine($"иконок: ok={ok}, нет в MPQ={miss}, ошибок декода={fail}");

        // Карта displayid → иконка (lower), только для тех, чья иконка реально извлечена.
        var existing = new HashSet<string>(
            Directory.GetFiles(iconsOutDir, "*.png").Select(f => Path.GetFileNameWithoutExtension(f)!));
        var sb = new StringBuilder();
        var written = 0;
        foreach (var kv in map.OrderBy(kv => kv.Key))
        {
            var icon = kv.Value.ToLowerInvariant();
            if (!existing.Contains(icon)) continue;
            sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(icon).Append('\n');
            written++;
        }
        File.WriteAllText(mapOutPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"карта displayid→иконка: {written} строк → {mapOutPath}");
    }
}
