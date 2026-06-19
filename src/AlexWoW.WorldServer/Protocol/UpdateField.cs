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
    // Первичные статы (OBJECT_END 0x06 + 0x4E..0x52). STAT2 = выносливость, STAT3 = интеллект. M9.2.
    public const int UnitStat0 = 0x0054;          // сила
    public const int UnitStat1 = 0x0055;          // ловкость
    public const int UnitStat2 = 0x0056;          // выносливость
    public const int UnitStat3 = 0x0057;          // интеллект
    public const int UnitStat4 = 0x0058;          // дух
    public const int UnitBaseMana = 0x0078;       // OBJECT_END + 0x72
    public const int UnitBaseHealth = 0x0079;     // OBJECT_END + 0x73
    /// <summary>UNIT_FIELD_RESISTANCES (OBJECT_END 0x06 + 0x5D), 7 полей: [0] броня, [1..6] резисты школ. M-защита.</summary>
    public const int UnitResistances = 0x0063;
    /// <summary>UNIT_FIELD_BYTES_2: sheath|pvpFlags|petFlags|shapeshiftForm. Байт 3 (старший) — форма
    /// (стойки воина/формы друида). M6.11.</summary>
    public const int UnitBytes2 = 0x007A;
    public const int UnitFlags = 0x003B;
    /// <summary>UNIT_FIELD_AURASTATE (3.3.5a OBJECT_END + 0x37): битовая маска состояний ауры. Бит 0 (value 1)
    /// = AURA_STATE_DEFENSE — выставлен на 5с после успешного dodge/parry/block игрока, позволяет Revenge.
    /// Бит 6 (value 0x40) = AURA_STATE_WARRIOR_VICTORY_RUSH / HUNTER_PARRY (Counterattack — пока не покрыто).
    /// Клиент серит/осветляет кнопку абилки по сравнению с CasterAuraState спелла. DEFENSE.1.</summary>
    public const int UnitAuraState = 0x003D;
    // Боевые поля (M9.2): чтобы чарпейн не показывал NaN-урон. BASEATTACKTIME size 2 (main 0x3E, off 0x3F).
    public const int UnitBaseAttackTime = 0x003E;
    public const int UnitMinDamage = 0x0046;      // float
    public const int UnitMaxDamage = 0x0047;      // float
    // Attack power (M7 #16): без этих полей слот-тултип оружия делит на percent=0 → 1.#INF.
    public const int UnitAttackPower = 0x007B;             // int
    public const int UnitAttackPowerMods = 0x007C;         // two int16 (pos|neg)
    public const int UnitAttackPowerMultiplier = 0x007D;   // float (TOTAL_PCT-1; обычно 0.0)
    public const int UnitRangedAttackPower = 0x007E;       // int
    public const int UnitRangedAttackPowerMods = 0x007F;   // two int16
    public const int UnitRangedAttackPowerMultiplier = 0x0080; // float
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

    // CONTAINER (сумки): ITEM_END = 0x40. NUM_SLOTS + слоты (guid на слот, stride 2). CMaNGOS UpdateFields.h (3.3.5a). M6.13.
    public const int ContainerNumSlots = 0x0040;  // CONTAINER_FIELD_NUM_SLOTS (size 1)
    public const int ContainerSlot1 = 0x0042;     // CONTAINER_FIELD_SLOT_1 (до 36 слотов × 2 поля)
    /// <summary>Поле guid предмета в слоте сумки 0..35 (CONTAINER_FIELD_SLOT_1 + i*2).</summary>
    public static int ContainerSlotGuid(int innerSlot) => ContainerSlot1 + innerSlot * 2;

    // GAMEOBJECT (OBJECT_END = 0x06)
    public const int GoDisplayId = 0x0008;
    public const int GoFlags = 0x0009;
    public const int GoParentRotation = 0x000A;  // 4 float (кватернион)
    public const int GoFaction = 0x000F;
    public const int GoBytes1 = 0x0011;           // state|type|artKit|animProgress

    // PLAYER
    /// <summary>Текущий опыт (UNIT_END 0x94 + 0x1E6). M9.1.</summary>
    public const int PlayerXp = 0x027A;
    /// <summary>Опыт до следующего уровня (0x94 + 0x1E7). M9.1.</summary>
    public const int PlayerNextLevelXp = 0x027B;
    /// <summary>Начало блока навыков (128 слотов × 3 поля). UNIT_END(0x94) + 0x1E8.</summary>
    public const int PlayerSkillInfo11 = 0x027C;

    /// <summary>Журнал квестов: 25 слотов × 5 полей (id, state, counters×2, time). 0x9E + 25*5 = 0x11B
    /// (= PlayerVisibleItem1 — сверка). M6.5.</summary>
    public const int PlayerQuestLog11 = 0x009E;
    public const int QuestLogSlotSize = 5;
    public const int QuestLogSlots = 25;
    /// <summary>Поле «id квеста» для слота журнала 0..24.</summary>
    public static int QuestLogSlotId(int slot) => PlayerQuestLog11 + slot * QuestLogSlotSize;
    /// <summary>Поле «состояние» слота (флаги; бит завершения).</summary>
    public static int QuestLogSlotState(int slot) => QuestLogSlotId(slot) + 1;
    /// <summary>Поле счётчиков целей 0/1 (две u16: obj0 low, obj1 high).</summary>
    public static int QuestLogSlotCounters01(int slot) => QuestLogSlotId(slot) + 2;
    /// <summary>Поле счётчиков целей 2/3 (две u16: obj2 low, obj3 high).</summary>
    public static int QuestLogSlotCounters23(int slot) => QuestLogSlotId(slot) + 3;
    public const int PlayerFlags = 0x0096;
    /// <summary>PLAYER_FIELD_BYTES (UNIT_END 0x94 + 0x419). Байт 2 — маска видимых доп. панелей
    /// (CMSG_SET_ACTIONBAR_TOGGLES). M7 #17.</summary>
    public const int PlayerFieldBytes = 0x04AD;
    /// <summary>PLAYER_FIELD_MOD_DAMAGE_DONE_PCT (UNIT_END 0x94 + 0x40D), 7 float по школам. Это «percent»
    /// в клиентском UnitDamage; по умолч. ДОЛЖЕН быть 1.0 (CMaNGOS), иначе слот-тултип оружия делит на 0 →
    /// 1.#INF. M7 #16.</summary>
    public const int PlayerFieldModDamageDonePct = 0x04A1;
    public const int PlayerBytes = 0x0099;        // skin|face|hairStyle|hairColor
    public const int PlayerBytes2 = 0x009A;       // facialHair|...|restState
    public const int PlayerBytes3 = 0x009B;       // gender|drunk

    /// <summary>Видимый предмет слота экипировки (одевает 3D-модель). 19 слотов, stride 2 (entry + enchant).</summary>
    public const int PlayerVisibleItem1EntryId = 0x011B;
    /// <summary>Слоты-контейнеры (guid предмета, 2 поля). Контигуально: экипировка 0..18, сумки, рюкзак 23..38.</summary>
    public const int PlayerFieldInvSlotHead = 0x0144;
    /// <summary>Деньги игрока (медь). Задел под торговлю (M6.2).</summary>
    public const int PlayerFieldCoinage = 0x0492;

    /// <summary>PLAYER_CHARACTER_POINTS1 — свободные очки талантов (private; сверено с CMaNGOS
    /// UpdateFields.cpp, калибровка по COINAGE=0x492). M9.6.</summary>
    public const int PlayerCharacterPoints1 = 0x03FC;

    /// <summary>PLAYER_RUNE_REGEN_1 (UNIT_END 0x94 + 0x485), 4 float по типам рун (Blood/Unholy/Frost/Death) —
    /// скорость регена руны (доля в секунду). Клиент DK без них не считает таймер восстановления рун. RUNE.1.</summary>
    public const int PlayerRuneRegen1 = 0x0519;

    // Вторичные защитные/боевые проценты (float). UNIT_END=0x94; сверено с эталоном UpdateFields.h.
    public const int PlayerBlockPercentage = 0x0400;    // UNIT_END + 0x36C
    public const int PlayerDodgePercentage = 0x0401;    // UNIT_END + 0x36D
    public const int PlayerParryPercentage = 0x0402;    // UNIT_END + 0x36E
    public const int PlayerCritPercentage = 0x0405;     // UNIT_END + 0x371 (мили)
    public const int PlayerRangedCritPercentage = 0x0406; // UNIT_END + 0x372
    public const int PlayerOffhandCritPercentage = 0x0407; // UNIT_END + 0x373

    /// <summary>Поле OBJECT_FIELD_ENTRYID видимого предмета для слота экипировки 0..18.</summary>
    public static int VisibleItemEntry(int equipSlot) => PlayerVisibleItem1EntryId + equipSlot * 2;

    /// <summary>Поле ENCHANTMENT видимого предмета (свечение временного энчанта оружия — яды/имбу, §8).
    /// На пару (EntryId, Enchantment) на слот; энчант — следующее поле за EntryId.</summary>
    public static int VisibleItemEnchant(int equipSlot) => PlayerVisibleItem1EntryId + equipSlot * 2 + 1;

    /// <summary>Поле guid предмета для слота-контейнера 0..38 (экипировка/сумки/рюкзак).</summary>
    public static int InvSlotGuid(int slot) => PlayerFieldInvSlotHead + slot * 2;
}

/// <summary>Флаги UNIT_FIELD_FLAGS (3.3.5a). Значения сверены с TrinityCore/CMaNGOS.</summary>
public static class UnitFlags
{
    /// <summary>UNIT_FLAG_PLAYER_CONTROLLED — юнит под управлением игрока. Клиент по нему выбирает
    /// ветку реакции/атаки PvC vs CvC; без него игрок считается существом и не может бить нейтралов.</summary>
    public const uint PlayerControlled = 0x00000008;

    // Флаги контроля (CC) — клиент рисует соответствующую анимацию/состояние. Значения 3.3.5a.
    public const uint Silenced = 0x00002000;   // UNIT_FLAG_SILENCED — нем (не может кастовать)
    public const uint Pacified = 0x00020000;   // UNIT_FLAG_PACIFIED
    public const uint Stunned = 0x00040000;    // UNIT_FLAG_STUNNED — оглушён (звёзды/нокдаун)
    public const uint Confused = 0x00400000;   // UNIT_FLAG_CONFUSED — дезориентирован (поли/блайнд)
    public const uint Fleeing = 0x00800000;    // UNIT_FLAG_FLEEING — в страхе
}

/// <summary>Типы объектов (OBJECT_FIELD_TYPE — битовая маска).</summary>
public static class TypeMask
{
    public const uint Object = 0x0001;
    public const uint Item = 0x0002;
    public const uint Container = 0x0004;
    public const uint Unit = 0x0008;
    public const uint Player = 0x0010;
    public const uint GameObject = 0x0020;

    /// <summary>Маска для предмета: Object | Item.</summary>
    public const uint ItemObject = Object | Item; // 0x03

    /// <summary>Маска для контейнера (сумки): Object | Item | Container.</summary>
    public const uint ContainerObject = Object | Item | Container; // 0x07

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
    public const byte Container = 2;   // TYPEID_CONTAINER (сумки) — сверено с CMaNGOS ObjectGuid.h
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
