using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Извлечение таблицы реакций фракций из клиентского <c>FactionTemplate.dbc</c> (WotLK 3.3.5a) —
/// для серверного авто-агро (M6.7 инкр.2b). В дампе мира этих данных нет (DBC клиентский).
/// Запись (WDBC), 14 полей × u32 = 56 байт: id, faction, flags, ourMask, friendMask, hostileMask,
/// enemy[4], friend[4]. Выход — SQL для таблицы <c>mangos.faction_template</c>.
/// Реакция «враждебно» (как в CMaNGOS): враг по списку enemy[] ИЛИ (hostileMask &amp; target.ourMask),
/// если не друг по friend[].
/// </summary>
public static class FactionTemplateExtract
{
    public static void ExtractToSql(MpqChain mpq, string outSqlPath)
    {
        var data = mpq.ReadFile("DBFilesClient\\FactionTemplate.dbc")
            ?? throw new FileNotFoundException("FactionTemplate.dbc не найден в MPQ");

        if (Encoding.ASCII.GetString(data, 0, 4) != "WDBC")
            throw new InvalidDataException("FactionTemplate.dbc: не WDBC");

        var recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var fieldCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        const int recordsOffset = 20;
        Console.WriteLine($"FactionTemplate.dbc: записей={recordCount}, полей={fieldCount}, размер записи={recordSize}");
        if (recordSize < 56)
            throw new InvalidDataException($"FactionTemplate.dbc: неожиданный размер записи {recordSize} (<56)");

        uint U(int rec, int field) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rec + field * 4));

        var sb = new StringBuilder();
        sb.AppendLine("-- FactionTemplate.dbc (3.3.5a) → faction_template для AlexWoW M6.7 (авто-агро).");
        sb.AppendLine("-- Залить: docker exec -i alexwow-mysql mysql -uroot -p<pw> mangos < factiontemplate.sql");
        sb.AppendLine("CREATE TABLE IF NOT EXISTS faction_template (");
        sb.AppendLine("  id INT UNSIGNED PRIMARY KEY, faction INT UNSIGNED NOT NULL,");
        sb.AppendLine("  ourMask INT UNSIGNED NOT NULL, friendMask INT UNSIGNED NOT NULL, hostileMask INT UNSIGNED NOT NULL,");
        sb.AppendLine("  enemy1 INT UNSIGNED NOT NULL, enemy2 INT UNSIGNED NOT NULL, enemy3 INT UNSIGNED NOT NULL, enemy4 INT UNSIGNED NOT NULL,");
        sb.AppendLine("  friend1 INT UNSIGNED NOT NULL, friend2 INT UNSIGNED NOT NULL, friend3 INT UNSIGNED NOT NULL, friend4 INT UNSIGNED NOT NULL");
        sb.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8;");
        sb.AppendLine("DELETE FROM faction_template;");

        var rows = 0;
        for (var i = 0; i < recordCount; i++)
        {
            var rec = recordsOffset + i * recordSize;
            var id = U(rec, 0);
            if (id == 0)
                continue;
            sb.Append("INSERT INTO faction_template VALUES (")
              .Append(id).Append(',').Append(U(rec, 1)).Append(',')          // faction
              .Append(U(rec, 3)).Append(',').Append(U(rec, 4)).Append(',').Append(U(rec, 5)).Append(',') // masks
              .Append(U(rec, 6)).Append(',').Append(U(rec, 7)).Append(',').Append(U(rec, 8)).Append(',').Append(U(rec, 9)).Append(',') // enemies
              .Append(U(rec, 10)).Append(',').Append(U(rec, 11)).Append(',').Append(U(rec, 12)).Append(',').Append(U(rec, 13)) // friends
              .AppendLine(");");
            rows++;
        }

        File.WriteAllText(outSqlPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"Готово: {rows.ToString(CultureInfo.InvariantCulture)} строк → {outSqlPath}");
    }
}
