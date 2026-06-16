namespace AlexWoW.Database.Models;

/// <summary>Игровой персонаж (минимальный набор для экрана выбора, веха M3).</summary>
public sealed class Character
{
    public uint Guid { get; init; }
    public uint AccountId { get; init; }
    public required string Name { get; init; }
    public byte Race { get; init; }
    public byte Class { get; init; }
    public byte Gender { get; init; }
    public byte Skin { get; init; }
    public byte Face { get; init; }
    public byte HairStyle { get; init; }
    public byte HairColor { get; init; }
    public byte FacialHair { get; init; }
    public byte Level { get; set; }    // M9.1: меняется при повышении уровня
    public uint Xp { get; set; }        // M9.1: текущий опыт на уровне
    public uint Zone { get; init; }
    public uint Map { get; set; }       // #79: меняется при кросс-карта телепорте (до пере-входа в мир)
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public uint Money { get; init; }   // медь (M6.2)
    public byte ActionBars { get; set; } // M7 #17: маска видимых доп. панелей (PLAYER_FIELD_BYTES[2])
    public uint TalentResetCost { get; set; } // M9.8: последняя стоимость сброса талантов (медь)
    public bool IsTester { get; set; }        // KB6: персонаж-тестировщик QA-доски
}
