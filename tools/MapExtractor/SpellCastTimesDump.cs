using System.Buffers.Binary;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Дамп <c>SpellCastTimes.dbc</c> (WotLK 3.3.5a) → C#-инициализатор словаря <c>index → base cast time (ms)</c>.
/// В дампе CMaNGOS <c>spell_template</c> хранится только <c>CastingTimeIndex</c> (ссылка на этот DBC), а не
/// само время каста — поэтому таблицу извлекаем офлайн и вшиваем константой в WorldServer (статичные
/// клиентские данные, не меняются между сборками клиента). Запись (WDBC): id u32, CastTime i32 (мс),
/// CastTimePerLevel i32, MinCastTime i32 → fieldCount=4, recordSize=16.
/// </summary>
public static class SpellCastTimesDump
{
    public static void Print(MpqChain mpq)
    {
        var data = mpq.ReadFile("DBFilesClient\\SpellCastTimes.dbc")
            ?? throw new FileNotFoundException("SpellCastTimes.dbc не найден в MPQ");
        if (System.Text.Encoding.ASCII.GetString(data, 0, 4) != "WDBC")
            throw new InvalidDataException("SpellCastTimes.dbc: не WDBC");

        var recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var fieldCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        const int recordsOffset = 20;
        Console.WriteLine($"// SpellCastTimes.dbc: записей={recordCount}, полей={fieldCount}, размер={recordSize}");

        for (var i = 0; i < recordCount; i++)
        {
            var rec = recordsOffset + i * recordSize;
            var id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rec));
            var baseMs = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(rec + 4));
            Console.WriteLine($"[{id}] = {baseMs},");
        }
    }
}
