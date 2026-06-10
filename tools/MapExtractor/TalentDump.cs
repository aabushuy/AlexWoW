using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Извлечение талантов из клиентских <c>Talent.dbc</c> + <c>TalentTab.dbc</c> (WotLK 3.3.5a) — для серверной
/// ВАЛИДАЦИИ изучения талантов (M9.7). В дампе мира этих данных нет (DBC клиентский). Деревья талантов клиент
/// рисует сам из своей DBC; серверу нужны: TalentTab (класс-маска, порядок) и Talent (tab/tier/col/ранг-спеллы/
/// пререквизиты). Сверено с CMaNGOS DBCStructure.h (TalentEntry/TalentTabEntry).
/// Выход — SQL для таблиц <c>mangos.talent</c> и <c>mangos.talent_tab</c>.
/// </summary>
public static class TalentDump
{
    public static void ExtractToSql(MpqChain mpq, string outSqlPath)
    {
        var talent = mpq.ReadFile("DBFilesClient\\Talent.dbc")
            ?? throw new FileNotFoundException("Talent.dbc не найден в MPQ");
        var tab = mpq.ReadFile("DBFilesClient\\TalentTab.dbc")
            ?? throw new FileNotFoundException("TalentTab.dbc не найден в MPQ");

        var sb = new StringBuilder();
        sb.AppendLine("-- Talent.dbc + TalentTab.dbc (3.3.5a) → talent/talent_tab для AlexWoW (таланты M9.6/M9.7).");
        sb.AppendLine("-- Залить: docker exec -i alexwow-mysql mysql -uroot -p<pw> mangos < talents.sql");

        var talents = WriteTalents(talent, sb);
        var tabs = WriteTabs(tab, sb);

        File.WriteAllText(outSqlPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"Готово: {talents} талантов + {tabs} вкладок → {outSqlPath}");
    }

    private static int WriteTalents(byte[] data, StringBuilder sb)
    {
        if (Encoding.ASCII.GetString(data, 0, 4) != "WDBC")
            throw new InvalidDataException("Talent.dbc: не WDBC");
        var recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        const int recordsOffset = 20;
        Console.WriteLine($"Talent.dbc: записей={recordCount}, размер записи={recordSize}");
        if (recordSize < 68) // нужны поля до DependsOnRank (field 16 = offset 64)
            throw new InvalidDataException($"Talent.dbc: неожиданный размер записи {recordSize} (<68)");

        uint U(int rec, int field) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rec + field * 4));

        // Tier/Col — Row/Col зарезервированы в MySQL → переименованы.
        sb.AppendLine("CREATE TABLE IF NOT EXISTS talent (");
        sb.AppendLine("  TalentID INT UNSIGNED PRIMARY KEY, TalentTab INT UNSIGNED NOT NULL,");
        sb.AppendLine("  Tier INT UNSIGNED NOT NULL, Col INT UNSIGNED NOT NULL,");
        sb.AppendLine("  RankID1 INT UNSIGNED NOT NULL, RankID2 INT UNSIGNED NOT NULL, RankID3 INT UNSIGNED NOT NULL,");
        sb.AppendLine("  RankID4 INT UNSIGNED NOT NULL, RankID5 INT UNSIGNED NOT NULL,");
        sb.AppendLine("  DependsOn INT UNSIGNED NOT NULL, DependsOnRank INT UNSIGNED NOT NULL");
        sb.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8;");
        sb.AppendLine("DELETE FROM talent;");

        var rows = 0;
        for (var i = 0; i < recordCount; i++)
        {
            var rec = recordsOffset + i * recordSize;
            var id = U(rec, 0);
            if (id == 0)
                continue;
            sb.Append("INSERT INTO talent VALUES (")
              .Append(id).Append(',').Append(U(rec, 1)).Append(',')         // TalentTab
              .Append(U(rec, 2)).Append(',').Append(U(rec, 3)).Append(',')   // Tier(Row), Col
              .Append(U(rec, 4)).Append(',').Append(U(rec, 5)).Append(',').Append(U(rec, 6)).Append(',')
              .Append(U(rec, 7)).Append(',').Append(U(rec, 8)).Append(',')   // RankID[0..4]
              .Append(U(rec, 13)).Append(',').Append(U(rec, 16))            // DependsOn, DependsOnRank
              .AppendLine(");");
            rows++;
        }
        return rows;
    }

    private static int WriteTabs(byte[] data, StringBuilder sb)
    {
        if (Encoding.ASCII.GetString(data, 0, 4) != "WDBC")
            throw new InvalidDataException("TalentTab.dbc: не WDBC");
        var recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        const int recordsOffset = 20;
        Console.WriteLine($"TalentTab.dbc: записей={recordCount}, размер записи={recordSize}");
        if (recordSize < 92) // нужны поля до OrderIndex (field 22 = offset 88)
            throw new InvalidDataException($"TalentTab.dbc: неожиданный размер записи {recordSize} (<92)");

        uint U(int rec, int field) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rec + field * 4));

        sb.AppendLine("CREATE TABLE IF NOT EXISTS talent_tab (");
        sb.AppendLine("  TalentTabID INT UNSIGNED PRIMARY KEY, ClassMask INT UNSIGNED NOT NULL, OrderIndex INT UNSIGNED NOT NULL");
        sb.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8;");
        sb.AppendLine("DELETE FROM talent_tab;");

        var rows = 0;
        for (var i = 0; i < recordCount; i++)
        {
            var rec = recordsOffset + i * recordSize;
            var id = U(rec, 0);
            if (id == 0)
                continue;
            sb.Append("INSERT INTO talent_tab VALUES (")
              .Append(id).Append(',').Append(U(rec, 20)).Append(',').Append(U(rec, 22)) // ClassMask, OrderIndex
              .AppendLine(");");
            rows++;
        }
        return rows;
    }
}
