using System.Buffers.Binary;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Дамп <c>SpellDuration.dbc</c> (WotLK 3.3.5a) → C#-инициализатор словаря <c>index → base duration (ms)</c>.
/// В дампе CMaNGOS <c>spell_template</c> хранится только <c>DurationIndex</c> (ссылка на этот DBC), а не сама
/// длительность ауры — нужна для DoT/HoT (M10.4b). Запись (WDBC): id u32, Duration i32 (мс),
/// DurationPerLevel i32, MaxDuration i32 → fieldCount=4, recordSize=16. Отрицательная длительность
/// (-1) = «до отмены» (бесконечная).
/// </summary>
public static class SpellDurationsDump
{
    public static void Print(MpqChain mpq)
    {
        var data = mpq.ReadFile("DBFilesClient\\SpellDuration.dbc")
            ?? throw new FileNotFoundException("SpellDuration.dbc не найден в MPQ");
        if (System.Text.Encoding.ASCII.GetString(data, 0, 4) != "WDBC")
            throw new InvalidDataException("SpellDuration.dbc: не WDBC");

        var recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var fieldCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        const int recordsOffset = 20;
        Console.WriteLine($"// SpellDuration.dbc: записей={recordCount}, полей={fieldCount}, размер={recordSize}");

        for (var i = 0; i < recordCount; i++)
        {
            var rec = recordsOffset + i * recordSize;
            var id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rec));
            var baseMs = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(rec + 4));
            Console.WriteLine($"[{id}] = {baseMs},");
        }
    }
}
