using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Извлечение GameTable-DBC боевых рейтингов (WotLK 3.3.5a) → JSON для WorldServer (срез защитных статов).
/// GameTable-DBC — плоские массивы float (без id/строк): запись = один float (fieldCount=1, recordSize=4).
///   <c>gtChanceToMeleeCritBase.dbc</c> — базовый крит на класс, индекс = class-1.
///   <c>gtChanceToMeleeCrit.dbc</c>     — крит на ед. ловкости, индекс = (class-1)*GT_MAX_LEVEL + (level-1).
/// Та же таблица crit-ratio даёт и уклонение-от-ловкости (эталон CMaNGOS Player::GetDodgeFromAgility).
/// Данные статичны для 3.3.5a → извлекаем офлайн и бандлим в DataStores.
/// </summary>
public static class CombatRatingDump
{
    private const int GtMaxLevel = 100;

    public static void ExtractToJson(MpqChain mpq, string outPath)
    {
        var critBase = ReadFloats(mpq, "DBFilesClient\\gtChanceToMeleeCritBase.dbc");
        var critRatio = ReadFloats(mpq, "DBFilesClient\\gtChanceToMeleeCrit.dbc");
        Console.WriteLine($"gtChanceToMeleeCritBase: {critBase.Length} записей; gtChanceToMeleeCrit: {critRatio.Length}");

        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"gtMaxLevel\": {GtMaxLevel},\n");
        AppendArray(sb, "critBase", critBase, last: false);
        AppendArray(sb, "critRatio", critRatio, last: true);
        sb.Append("}\n");
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"Записано: {outPath}");
    }

    private static void AppendArray(StringBuilder sb, string name, float[] vals, bool last)
    {
        sb.Append($"  \"{name}\": [");
        for (var i = 0; i < vals.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            // R — round-trip формат float, InvariantCulture (точка-десятичная).
            sb.Append(vals[i].ToString("R", CultureInfo.InvariantCulture));
        }
        sb.Append(last ? "]\n" : "],\n");
    }

    /// <summary>Читает GameTable-DBC как массив float (берём последние 4 байта записи — на случай id-префикса).</summary>
    private static float[] ReadFloats(MpqChain mpq, string name)
    {
        var data = mpq.ReadFile(name) ?? throw new FileNotFoundException($"{name} не найден в MPQ");
        if (Encoding.ASCII.GetString(data, 0, 4) != "WDBC")
            throw new InvalidDataException($"{name}: не WDBC");

        var recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        const int recordsOffset = 20;

        var result = new float[recordCount];
        for (var i = 0; i < recordCount; i++)
        {
            var off = recordsOffset + i * recordSize + (recordSize - 4);
            result[i] = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(off));
        }
        return result;
    }
}
