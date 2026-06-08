using System.Buffers.Binary;
using System.Text;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Диагностика клиентского <c>Faction.dbc</c> (WotLK 3.3.5a) — печать reputationListID и базовых
/// standing'ов для заданных id факций. Нужно для сверки реакций существ (M7 #11): у факций с
/// reputationListID &gt;= 0 реакция клиента считается по репутации, а не по маскам FactionTemplate.
/// Запись (WDBC): id(0), reputationListID(1, int32), BaseRepRaceMask[4](2-5), BaseRepClassMask[4](6-9),
/// BaseRepValue[4](10-13, int32), ReputationFlags[4](14-17), parentFactionID(18), ...
/// </summary>
public static class FactionDump
{
    /// <summary>Печать записей FactionTemplate.dbc (id, faction, flags, маски, enemy/friend) для сверки —
    /// в частности factionFlags (field 2), который наш SQL-экстрактор пропускает.</summary>
    public static void PrintTemplates(MpqChain mpq, IReadOnlyList<uint> ids)
    {
        var data = mpq.ReadFile("DBFilesClient\\FactionTemplate.dbc")
            ?? throw new FileNotFoundException("FactionTemplate.dbc не найден в MPQ");
        if (Encoding.ASCII.GetString(data, 0, 4) != "WDBC")
            throw new InvalidDataException("FactionTemplate.dbc: не WDBC");

        var recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        const int recordsOffset = 20;

        uint U(int rec, int field) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rec + field * 4));

        var want = new HashSet<uint>(ids);
        Console.WriteLine($"\nFactionTemplate.dbc (записей={recordCount}, размер={recordSize}):");
        for (var i = 0; i < recordCount; i++)
        {
            var rec = recordsOffset + i * recordSize;
            var id = U(rec, 0);
            if (!want.Contains(id))
                continue;
            Console.WriteLine($"  template {id}: faction={U(rec, 1)} flags=0x{U(rec, 2):X} "
                + $"ourMask=0x{U(rec, 3):X} friendMask=0x{U(rec, 4):X} hostileMask=0x{U(rec, 5):X} "
                + $"enemy=[{U(rec, 6)},{U(rec, 7)},{U(rec, 8)},{U(rec, 9)}] "
                + $"friend=[{U(rec, 10)},{U(rec, 11)},{U(rec, 12)},{U(rec, 13)}]");
        }
    }

    public static void Print(MpqChain mpq, IReadOnlyList<uint> ids)
    {
        var data = mpq.ReadFile("DBFilesClient\\Faction.dbc")
            ?? throw new FileNotFoundException("Faction.dbc не найден в MPQ");

        if (Encoding.ASCII.GetString(data, 0, 4) != "WDBC")
            throw new InvalidDataException("Faction.dbc: не WDBC");

        var recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var fieldCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        const int recordsOffset = 20;
        Console.WriteLine($"Faction.dbc: записей={recordCount}, полей={fieldCount}, размер записи={recordSize}");

        int U(int rec, int field) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(rec + field * 4));

        var want = new HashSet<uint>(ids);
        for (var i = 0; i < recordCount; i++)
        {
            var rec = recordsOffset + i * recordSize;
            var id = (uint)U(rec, 0);
            if (!want.Contains(id))
                continue;

            var repIdx = U(rec, 1);
            Console.WriteLine($"\n=== Faction {id}: reputationListID={repIdx} "
                + $"({(repIdx >= 0 ? "ИМЕЕТ репутацию" : "БЕЗ репутации")}) ===");
            for (var s = 0; s < 4; s++)
            {
                var raceMask = (uint)U(rec, 2 + s);
                var classMask = (uint)U(rec, 6 + s);
                var baseValue = U(rec, 10 + s);
                var flags = (uint)U(rec, 14 + s);
                if (raceMask == 0 && classMask == 0 && baseValue == 0 && flags == 0)
                    continue;
                Console.WriteLine($"  slot{s}: raceMask=0x{raceMask:X} classMask=0x{classMask:X} "
                    + $"base={baseValue} flags=0x{flags:X}");
            }
        }
    }
}
