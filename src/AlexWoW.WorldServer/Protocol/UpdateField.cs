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
    public const int UnitDynamicFlags = 0x004F;   // UNIT_DYNAMIC_FLAGS — LOOTABLE(0x1) и пр. (труп-лут M6.6)
    public const int UnitNpcFlags = 0x0052;       // OBJECT_END(0x06)+0x4C — иконки госсипа/вендора/квестов
    public const int UnitBoundingRadius = 0x0041; // float
    public const int UnitCombatReach = 0x0042;    // float
    public const int UnitDisplayId = 0x0043;
    public const int UnitNativeDisplayId = 0x0044;
    /// <summary>UNIT_MOD_CAST_SPEED (float, по умолчанию 1.0). Клиент масштабирует им анимации/время
    /// каста — без него (0.0) анимация каста ломается (не стартует/залипает). Сверено с reference (3.3.5).</summary>
    public const int UnitModCastSpeed = 0x0050;

    // ITEM (OBJECT_END = 0x06). Значения сверены с TrinityCore/CMaNGOS UpdateFields.h (3.3.5a).
    public const int ItemOwner = 0x0006;          // size 2 (guid владельца)
    public const int ItemContained = 0x0008;      // size 2 (guid контейнера; рюкзак = guid игрока)
    public const int ItemStackCount = 0x000E;
    public const int ItemDurability = 0x003C;
    public const int ItemMaxDurability = 0x003D;

    // GAMEOBJECT (OBJECT_END = 0x06)
    public const int GoDisplayId = 0x0008;
    public const int GoFlags = 0x0009;
    public const int GoParentRotation = 0x000A;  // 4 float (кватернион)
    public const int GoFaction = 0x000F;
    public const int GoBytes1 = 0x0011;           // state|type|artKit|animProgress

    // PLAYER
    /// <summary>Начало блока навыков (128 слотов × 3 поля). UNIT_END(0x94) + 0x1E8.</summary>
    public const int PlayerSkillInfo11 = 0x027C;

    /// <summary>Журнал квестов: 25 слотов × 5 полей (id, state, counters×2, time). 0x9E + 25*5 = 0x11B
    /// (= PlayerVisibleItem1 — сверка). M6.5.</summary>
    public const int PlayerQuestLog11 = 0x009E;
    public const int QuestLogSlotSize = 5;
    public const int QuestLogSlots = 25;
    /// <summary>Поле «id квеста» для слота журнала 0..24.</summary>
    public static int QuestLogSlotId(int slot) => PlayerQuestLog11 + slot * QuestLogSlotSize;
    public const int PlayerFlags = 0x0096;
    public const int PlayerBytes = 0x0099;        // skin|face|hairStyle|hairColor
    public const int PlayerBytes2 = 0x009A;       // facialHair|...|restState
    public const int PlayerBytes3 = 0x009B;       // gender|drunk

    /// <summary>Видимый предмет слота экипировки (одевает 3D-модель). 19 слотов, stride 2 (entry + enchant).</summary>
    public const int PlayerVisibleItem1EntryId = 0x011B;
    /// <summary>Слоты-контейнеры (guid предмета, 2 поля). Контигуально: экипировка 0..18, сумки, рюкзак 23..38.</summary>
    public const int PlayerFieldInvSlotHead = 0x0144;
    /// <summary>Деньги игрока (медь). Задел под торговлю (M6.2).</summary>
    public const int PlayerFieldCoinage = 0x0492;

    /// <summary>Поле OBJECT_FIELD_ENTRYID видимого предмета для слота экипировки 0..18.</summary>
    public static int VisibleItemEntry(int equipSlot) => PlayerVisibleItem1EntryId + equipSlot * 2;

    /// <summary>Поле guid предмета для слота-контейнера 0..38 (экипировка/сумки/рюкзак).</summary>
    public static int InvSlotGuid(int slot) => PlayerFieldInvSlotHead + slot * 2;
}

/// <summary>Типы объектов (OBJECT_FIELD_TYPE — битовая маска).</summary>
public static class TypeMask
{
    public const uint Object = 0x0001;
    public const uint Item = 0x0002;
    public const uint Unit = 0x0008;
    public const uint Player = 0x0010;
    public const uint GameObject = 0x0020;

    /// <summary>Маска для предмета: Object | Item.</summary>
    public const uint ItemObject = Object | Item; // 0x03

    /// <summary>Маска для игрока: Object | Unit | Player.</summary>
    public const uint PlayerObject = Object | Unit | Player; // 0x19

    /// <summary>Маска для существа (NPC): Object | Unit.</summary>
    public const uint UnitObject = Object | Unit; // 0x09

    /// <summary>Маска для гейм-объекта: Object | GameObject.</summary>
    public const uint GameObjectObject = Object | GameObject; // 0x21
}

/// <summary>TypeId объекта в блоке create.</summary>
public static class TypeId
{
    public const byte Item = 1;
    public const byte Unit = 3;
    public const byte Player = 4;
    public const byte GameObject = 5;
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
    HighGuid = 0x0010,            // followed by uint32 high-part of GUID (предметы и др. неживые)
    Living = 0x0020,
    StationaryPosition = 0x0040,
    Rotation = 0x0200,            // упакованный кватернион (int64) — для гейм-объектов
}
