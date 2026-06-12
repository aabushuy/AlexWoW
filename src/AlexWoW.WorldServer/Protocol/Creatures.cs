namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Шаблон существа — минимум для отображения и ответа на <c>CMSG_CREATURE_QUERY</c>.
/// Аналог строки <c>creature_template</c> в дампе мира (позже M5.4 подтянем из MySQL).
/// </summary>
public sealed record CreatureTemplate(
    uint Entry,
    string Name,
    string SubName,
    uint DisplayId,
    byte Level,
    uint Faction,
    uint UnitType,          // CreatureType: 7 = Humanoid
    float Scale = 1.0f,
    uint NpcFlags = 0,
    byte UnitClass = 1);    // 1 = Warrior

/// <summary>Реестр существ и помощники по GUID (M5.1 — пока один тестовый NPC).</summary>
public static class Npcs
{
    /// <summary>HIGHGUID_UNIT (3.3.5a) — старшие 16 бит GUID существа.</summary>
    public const ulong HighGuidUnit = 0xF130;

    /// <summary>
    /// Сборка GUID существа: <c>0xF130 | entry &lt;&lt; 24 | counter</c>.
    /// counter — низкий 24-битный id спавна (в дампе — поле guid таблицы creature).
    /// </summary>
    public static ulong UnitGuid(uint entry, uint counter)
        => (HighGuidUnit << 48) | ((ulong)entry << 24) | counter;

    /// <summary>
    /// Тестовый NPC: модель цыплёнка (display 257 — настоящая creature-модель со своей
    /// текстурой, в отличие от моделей рас, которые без кастомизации рисуются белыми).
    /// Временный плейсхолдер: реальные модели придут с дампом мира (M5.4).
    /// </summary>
    public static readonly CreatureTemplate TestDummy = new(
        Entry: 190000,
        Name: "Тестовый цыплёнок",
        SubName: "AlexWoW",
        DisplayId: 257,
        Level: 1,
        Faction: 35,   // «дружелюбен ко всем» — нейтральный, не атакует
        UnitType: 8);  // Critter

    /// <summary>
    /// Entry кастомного тренировочного манекена (#28): спавнится из БД мира (creature 990020,
    /// клон Advanced Training Dummy), но код даёт ему большой HP и делает ПАССИВНЫМ (не авто-агрится
    /// и не отвечает в бою) — стационарная цель для проверки навыков.
    /// </summary>
    public const uint TrainingDummyEntry = 990020;

    /// <summary>HP манекена — заведомо много, чтобы переживал любые тесты (не из формулы по уровню). #28.</summary>
    public const uint TrainingDummyHealth = 50_000_000;

    /// <summary>Counter (creature.guid в БД) статичного спавна манекена в Нортшире. #28/#29.</summary>
    public const uint TrainingDummySpawnId = 9000020;

    /// <summary>GUID существа манекена — тот же, что у БД-спавна; дев-команда .dummy двигает его. #29.</summary>
    public static readonly ulong TrainingDummyGuid = UnitGuid(TrainingDummyEntry, TrainingDummySpawnId);

    /// <summary>Существо — тренировочный (урон) манекен (пассивная высоко-HP цель). #28.</summary>
    public static bool IsTrainingDummy(uint entry) => entry == TrainingDummyEntry;

    /// <summary>Шаблон манекена — фоллбэк для дев-команды .dummy, если он ещё не закэширован из БД. #29.</summary>
    public static readonly CreatureTemplate TrainingDummy = new(
        Entry: TrainingDummyEntry,
        Name: "Тренировочный манекен",
        SubName: "Уровень 80 — проверка урона",
        DisplayId: 3019,
        Level: 80,
        Faction: 7,    // нейтральный (жёлтый) — атакуемый, не авто-враждебный
        UnitType: 7);  // Humanoid

    /// <summary>Entry лечебного манекена (M12 Spell QA): дружественная цель для проверки хилов/HoT.</summary>
    public const uint HealDummyEntry = 990021;

    /// <summary>Макс. HP лечебного манекена (как у урон-манекена — большой, чтобы держался любой тест). M12.</summary>
    public const uint HealDummyHealth = 50_000_000;

    /// <summary>Counter (creature.guid) спавна лечебного манекена. M12.</summary>
    public const uint HealDummySpawnId = 9000021;

    /// <summary>GUID лечебного манекена; дев-команда .dummy heal двигает/призывает его. M12.</summary>
    public static readonly ulong HealDummyGuid = UnitGuid(HealDummyEntry, HealDummySpawnId);

    /// <summary>Существо — лечебный манекен (дружественная цель хила, всегда ранен). M12.</summary>
    public static bool IsHealDummy(uint entry) => entry == HealDummyEntry;

    /// <summary>Любой тестовый манекен (урон или хил) — пассивный, высоко-HP. M12.</summary>
    public static bool IsTestDummy(uint entry) => IsTrainingDummy(entry) || IsHealDummy(entry);

    /// <summary>Entry атакующего манекена (проверка защиты): уровень 80, ОТВЕЧАЕТ в бою (не пассивен) —
    /// бьёт игрока при атаке, чтобы видеть уклонение/парирование/блок/броню/«Глухую оборону».</summary>
    public const uint AttackDummyEntry = 990022;
    public const uint AttackDummyHealth = 50_000_000;
    public const uint AttackDummySpawnId = 9000022;
    public static readonly ulong AttackDummyGuid = UnitGuid(AttackDummyEntry, AttackDummySpawnId);

    /// <summary>Существо — атакующий манекен (отвечает в бою; НЕ входит в IsTestDummy → не пассивен).</summary>
    public static bool IsAttackDummy(uint entry) => entry == AttackDummyEntry;

    /// <summary>Шаблон атакующего манекена. Faction 7 (нейтрал, атакуемый); т.к. не IsTestDummy — отвечает при атаке.</summary>
    public static readonly CreatureTemplate AttackDummy = new(
        Entry: AttackDummyEntry,
        Name: "Атакующий манекен",
        SubName: "Уровень 80 — проверка защиты",
        DisplayId: 3019,
        Level: 80,
        Faction: 7,    // нейтральный (жёлтый) — атакуемый; ответка при атаке
        UnitType: 7);  // Humanoid

    /// <summary>Шаблон лечебного манекена. Faction 35 (дружелюбен ко всем) — не атакуем, валидная цель хила. M12.</summary>
    public static readonly CreatureTemplate HealDummy = new(
        Entry: HealDummyEntry,
        Name: "Лечебный манекен",
        SubName: "Уровень 80 — проверка лечения",
        DisplayId: 3019,
        Level: 80,
        Faction: 35,   // дружелюбен ко всем — не атакуем; принимает лечащие спеллы
        UnitType: 7);  // Humanoid

    private static readonly Dictionary<uint, CreatureTemplate> ByEntry = new()
    {
        [TestDummy.Entry] = TestDummy,
    };

    public static CreatureTemplate? Find(uint entry)
        => ByEntry.TryGetValue(entry, out var template) ? template : null;
}
