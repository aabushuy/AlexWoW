namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Индексы полей объекта в update-mask (WotLK 3.3.5a, build 12340).
/// Значения — абсолютные индексы из UpdateFields.h. Менять нельзя — клиент завязан на них.
/// </summary>
public static class UpdateField
{
    // OBJECT
    public const int ObjectGuid = 0x0000;        // size 2
    public const int ObjectType = 0x0002;        // size 1 (type mask)
    public const int ObjectEntry = 0x0003;       // entry шаблона (для существ/гейм-объектов)
    public const int ObjectScaleX = 0x0004;      // float

    // UNIT
    public const int UnitBytes0 = 0x0017;        // race|class|gender|powertype
    public const int UnitHealth = 0x0018;
    public const int UnitPower1 = 0x0019;
    public const int UnitMaxHealth = 0x0020;
    public const int UnitMaxPower1 = 0x0021;
    public const int UnitLevel = 0x0036;
    public const int UnitFactionTemplate = 0x0037;
    public const int UnitFlags = 0x003B;
    public const int UnitNpcFlags = 0x0052;       // OBJECT_END(0x06)+0x4C — иконки госсипа/вендора/квестов
    public const int UnitBoundingRadius = 0x0041; // float
    public const int UnitCombatReach = 0x0042;    // float
    public const int UnitDisplayId = 0x0043;
    public const int UnitNativeDisplayId = 0x0044;

    // PLAYER
    /// <summary>Начало блока навыков (128 слотов × 3 поля). UNIT_END(0x94) + 0x1E8.</summary>
    public const int PlayerSkillInfo11 = 0x027C;
    public const int PlayerFlags = 0x0096;
    public const int PlayerBytes = 0x0099;        // skin|face|hairStyle|hairColor
    public const int PlayerBytes2 = 0x009A;       // facialHair|...|restState
    public const int PlayerBytes3 = 0x009B;       // gender|drunk
}

/// <summary>Типы объектов (OBJECT_FIELD_TYPE — битовая маска).</summary>
public static class TypeMask
{
    public const uint Object = 0x0001;
    public const uint Unit = 0x0008;
    public const uint Player = 0x0010;

    /// <summary>Маска для игрока: Object | Unit | Player.</summary>
    public const uint PlayerObject = Object | Unit | Player; // 0x19

    /// <summary>Маска для существа (NPC): Object | Unit.</summary>
    public const uint UnitObject = Object | Unit; // 0x09
}

/// <summary>TypeId объекта в блоке create.</summary>
public static class TypeId
{
    public const byte Unit = 3;
    public const byte Player = 4;
}

/// <summary>Типы блоков в SMSG_UPDATE_OBJECT.</summary>
public static class UpdateType
{
    public const byte Values = 0;
    public const byte CreateObject = 2;
    public const byte CreateObject2 = 3; // для себя
}

/// <summary>Флаги движения в блоке create (uint16).</summary>
[Flags]
public enum ObjectUpdateFlags : ushort
{
    None = 0x0000,
    Self = 0x0001,
    Living = 0x0020,
    StationaryPosition = 0x0040,
}
