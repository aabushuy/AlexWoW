using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Извлечение иконок СПЕЛЛОВ из клиента 3.3.5a (полный офлайн) для канбан-доски AlexWoW.Web.
/// SpellIcon.dbc даёт SpellIconID → путь к иконке (поле 1 = TextureFilename,
/// типа "Interface\Icons\Spell_Fire_Flamebolt"). BLP-иконки конвертируются в PNG в общую
/// папку wwwroot/icons/ (та же, что у предметов — много иконок переиспользуется).
/// Выход: PNG в wwwroot/icons/ (только новые, существующие пропускаются), карта
/// SpellIconID → имя иконки в wwwroot/icons/_spell-map.tsv (отдельно от _map.tsv предметов).
/// </summary>
public static class SpellIconMapDump
{
    public static void Extract(MpqChain mpq, string iconsOutDir, string mapOutPath)
    {
        var data = mpq.ReadFile("DBFilesClient\\SpellIcon.dbc")
            ?? throw new FileNotFoundException("SpellIcon.dbc не найден в MPQ");
        if (Encoding.ASCII.GetString(data, 0, 4) != "WDBC")
            throw new InvalidDataException("SpellIcon.dbc: не WDBC");

        var recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        const int recordsOffset = 20;
        var stringStart = recordsOffset + recordCount * recordSize;
        Console.WriteLine($"SpellIcon.dbc: записей={recordCount}, размер записи={recordSize}");

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

        // SpellIcon.dbc: field 0 = ID, field 1 = TextureFilename ref. Имя приходит как
        // "Interface\\Icons\\Spell_Fire_Flamebolt" — вырезаем только базовое имя файла (без пути и расширения),
        // в lower-case, как и для предметов.
        var map = new Dictionary<uint, string>();
        for (var i = 0; i < recordCount; i++)
        {
            var rec = recordsOffset + i * recordSize;
            var id = U(rec, 0);
            if (id == 0) continue;
            var path = Str(rec, 1);
            if (string.IsNullOrEmpty(path)) continue;
            var name = Path.GetFileNameWithoutExtension(path.Replace('\\', '/')).ToLowerInvariant();
            if (name.Length > 0)
                map[id] = name;
        }
        Console.WriteLine($"SpellIconID с иконкой: {map.Count}");

        // Конвертируем уникальные BLP → PNG. Пропускаем существующие (часть уже есть от IconMapDump).
        Directory.CreateDirectory(iconsOutDir);
        var existing = new HashSet<string>(
            Directory.GetFiles(iconsOutDir, "*.png").Select(f => Path.GetFileNameWithoutExtension(f)!),
            StringComparer.OrdinalIgnoreCase);

        var unique = map.Values.Distinct().ToList();
        Console.WriteLine($"уникальных имён иконок: {unique.Count} (из них уже есть: {unique.Count(existing.Contains)})");

        int ok = 0, miss = 0, fail = 0, skip = 0;
        foreach (var icon in unique)
        {
            if (existing.Contains(icon)) { skip++; continue; }
            var blp = mpq.ReadFile($"Interface\\Icons\\{icon}.blp");
            if (blp is null) { miss++; continue; }
            try
            {
                var (w, h, rgba) = Blp.Decode(blp);
                Png.Write(Path.Combine(iconsOutDir, icon + ".png"), w, h, rgba);
                existing.Add(icon);
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                if (fail <= 10) Console.WriteLine($"  ! {icon}: {ex.Message}");
            }
        }
        Console.WriteLine($"иконок: новых={ok}, уже было={skip}, нет в MPQ={miss}, ошибок декода={fail}");

        // Карта SpellIconID → имя — только для тех, чья иконка реально есть.
        var sb = new StringBuilder();
        var written = 0;
        foreach (var kv in map.OrderBy(kv => kv.Key))
        {
            if (!existing.Contains(kv.Value)) continue;
            sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(kv.Value).Append('\n');
            written++;
        }
        File.WriteAllText(mapOutPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"карта SpellIconID→иконка: {written} строк → {mapOutPath}");
    }
}
