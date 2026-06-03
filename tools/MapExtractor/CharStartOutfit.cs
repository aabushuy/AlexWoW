using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Извлечение стартовой экипировки из клиентского <c>CharStartOutfit.dbc</c> (WotLK 3.3.5a).
/// CMaNGOS-дамп держит <c>playercreateinfo_item</c> пустой — базовый outfit живёт в этом DBC.
/// Запись (WDBC): id u32, packed u32 (byte0=race, byte1=class, byte2=gender, byte3=outfitId),
/// itemId[24] i32, displayId[24] i32, invType[24] i32 → recordSize = 4 + 4 + 24*4*3 = 296.
/// Выход — SQL INSERT для <c>mangos.playercreateinfo_item (race,class,itemid,amount)</c>.
/// </summary>
public static class CharStartOutfit
{
    public static void ExtractToSql(MpqChain mpq, string outSqlPath)
    {
        var data = mpq.ReadFile("DBFilesClient\\CharStartOutfit.dbc")
            ?? throw new FileNotFoundException("CharStartOutfit.dbc не найден в MPQ");

        if (Encoding.ASCII.GetString(data, 0, 4) != "WDBC")
            throw new InvalidDataException("CharStartOutfit.dbc: не WDBC");

        var recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var fieldCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        var recordsOffset = 20;
        Console.WriteLine($"CharStartOutfit.dbc: записей={recordCount}, полей={fieldCount}, размер записи={recordSize}");

        // (race,class) → set itemId (дедуп; itemId слабо зависит от пола — берём объединение).
        var byRaceClass = new SortedDictionary<(byte Race, byte Class), SortedSet<int>>();
        var rows = 0;

        for (var i = 0; i < recordCount; i++)
        {
            var rec = recordsOffset + i * recordSize;
            var packed = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rec + 4));
            var race = (byte)(packed & 0xFF);
            var cls = (byte)((packed >> 8) & 0xFF);
            if (race == 0 || cls == 0)
                continue; // служебные/пустые

            var key = (race, cls);
            if (!byRaceClass.TryGetValue(key, out var items))
                byRaceClass[key] = items = new SortedSet<int>();

            for (var s = 0; s < 24; s++)
            {
                var itemId = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(rec + 8 + s * 4));
                if (itemId > 0)
                    items.Add(itemId);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("-- Стартовая экипировка из CharStartOutfit.dbc (3.3.5a) для AlexWoW M6.1.");
        sb.AppendLine("-- Залить в БД mangos: docker exec -i alexwow-mysql mysql -uroot -p<pw> mangos < charstartoutfit.sql");
        sb.AppendLine("DELETE FROM playercreateinfo_item;");
        foreach (var ((race, cls), items) in byRaceClass)
            foreach (var itemId in items)
            {
                sb.Append("INSERT INTO playercreateinfo_item (race,class,itemid,amount) VALUES (")
                  .Append(race.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(cls.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(itemId.ToString(CultureInfo.InvariantCulture)).AppendLine(",1);");
                rows++;
            }

        File.WriteAllText(outSqlPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"Готово: {rows} строк по {byRaceClass.Count} (раса,класс) → {outSqlPath}");
    }
}
