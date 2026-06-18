using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Дамп SkillLineAbility.dbc → JSON: spell_id → skill_line_id. Нужен веб-инструментам
/// (tools/regression-import/migrate-professions.py) — определять, к какой профессии
/// относится спелл, без поднятия SkillLine.dbc на сервере.
///
/// SkillLineAbility.dbc layout (WotLK 3.3.5a):
///   [0] ID, [1] SkillLine, [2] Spell, [3] RaceMask, [4] ClassMask, [5] ExcludeRace, [6] ExcludeClass,
///   [7] MinSkill, [8] Supercedes, [9] AcquireMethod, [10] TrivialSkillLineRankHigh,
///   [11] TrivialSkillLineRankLow, [12] CharacterPoints[0..1] …
/// Берём только Spell и SkillLine — для маппинга достаточно.
/// </summary>
public static class SkillLineAbilityDump
{
    public static void Extract(MpqChain mpq, string outJsonPath)
    {
        var data = mpq.ReadFile("DBFilesClient\\SkillLineAbility.dbc")
            ?? throw new FileNotFoundException("SkillLineAbility.dbc не найден в MPQ");
        if (Encoding.ASCII.GetString(data, 0, 4) != "WDBC")
            throw new InvalidDataException("SkillLineAbility.dbc: не WDBC");

        var recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        const int recordsOffset = 20;
        Console.WriteLine($"SkillLineAbility.dbc: записей={recordCount}, размер записи={recordSize}");

        uint U(int rec, int field) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rec + field * 4));

        // spell_id может встречаться несколько раз с разными skill_line (например Mining + Smelting).
        // Сохраним все пары — потребитель выберет нужный навык по своему вайтлисту.
        var sb = new StringBuilder(recordCount * 24);
        sb.Append('{');
        var first = true;
        var bySpell = new Dictionary<uint, List<uint>>();
        for (var i = 0; i < recordCount; i++)
        {
            var rec = recordsOffset + i * recordSize;
            var skill = U(rec, 1);
            var spell = U(rec, 2);
            if (spell == 0) continue;
            if (!bySpell.TryGetValue(spell, out var list))
                bySpell[spell] = list = new List<uint>();
            if (!list.Contains(skill)) list.Add(skill);
        }
        foreach (var kv in bySpell.OrderBy(kv => kv.Key))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append("\":[");
            for (var j = 0; j < kv.Value.Count; j++)
            {
                if (j > 0) sb.Append(',');
                sb.Append(kv.Value[j].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(']');
        }
        sb.Append('}');

        File.WriteAllText(outJsonPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"карта spell_id→[skill_line]: {bySpell.Count} записей → {outJsonPath}");
    }
}
