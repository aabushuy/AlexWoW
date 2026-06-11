namespace AlexWoW.Database.Entities;

/// <summary>
/// EF-сущность таблицы <c>spell_test_result</c> (БД alexwow_auth) — одна запись применённого эффекта
/// заклинания в сессии захвата (M12 Spell QA): прямой урон/хил или тик DoT/HoT. Строка самоописательна —
/// эталон (<see cref="ExpectedMin"/>/<see cref="ExpectedMax"/>, школа, стоимость) сохраняется в момент
/// захвата на world-сервере, т.к. Web не имеет доступа к <c>spell_template</c> (БД mangos).
/// </summary>
public sealed class SpellTestResult
{
    public long Id { get; set; }                 // PK (auto-increment)
    public long SessionId { get; set; }          // FK → spell_test_session.id (индекс)
    public uint SpellId { get; set; }
    public byte Class { get; set; }              // денормализация (без джойна для Web)
    public byte Level { get; set; }
    public byte ResultType { get; set; }         // 0=DirectDamage,1=DirectHeal,2=DotTick,3=HotTick
    public byte School { get; set; }             // SpellSchoolMask (info.School)
    public uint Amount { get; set; }             // вычисленная величина (урон/хил до капа)
    public uint Effective { get; set; }          // фактически применено (после капа HP)
    public uint OverkillOrOverheal { get; set; } // овёркилл (урон) / овёрхил (хил)
    public uint ExpectedMin { get; set; }        // эталонный минимум (info.MinAmount / TickAmount)
    public uint ExpectedMax { get; set; }        // эталонный максимум (info.MaxAmount / TickAmount)
    public uint ExpectedCost { get; set; }       // ожидаемая стоимость ресурса (EffectivePowerCost)
    public byte PowerType { get; set; }          // 0=мана,1=ярость,3=энергия
    public byte IsHeal { get; set; }             // 1 — спелл лечащий
    public byte WeaponBased { get; set; }        // 1 — урон зависит от оружия (amount вне min/max — норма)
    public uint FamilyName { get; set; }         // SpellFamilyName (группировка в UI)
    public ushort CastIndex { get; set; }        // номер каста в харнессе (0..N-1); 0 для ручного
    public DateTime RecordedAt { get; set; }     // время записи (UTC)
}
