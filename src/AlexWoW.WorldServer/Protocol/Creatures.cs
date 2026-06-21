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

    /// <summary>Entry кастующего манекена (Фаза 2 INT.1): крутит каст-бар по игроку — стенд для проверки
    /// прерывания (Kick/Counterspell/Pummel). Урон по игроку не наносит (каст «вхолостую»).</summary>
    public const uint CasterDummyEntry = 990023;
    public const uint CasterDummyHealth = 50_000_000;
    public const uint CasterDummySpawnId = 9000023;
    public static readonly ulong CasterDummyGuid = UnitGuid(CasterDummyEntry, CasterDummySpawnId);

    /// <summary>Существо — кастующий манекен (зацикленный каст для проверки interrupt). INT.1.</summary>
    public static bool IsCasterDummy(uint entry) => entry == CasterDummyEntry;

    /// <summary>Спелл, который крутит кастующий манекен: Ледяная стрела (Frost, школа 16). Каст-тайм и пауза —
    /// фиксированные (см. CreatureCombatAI), чтобы прерывание было удобно ловить. INT.1.</summary>
    public const uint CasterDummyCastSpellId = 116;
    public const byte CasterDummyCastSchoolMask = 16; // SCHOOL_MASK_FROST

    /// <summary>Снимаемый Magic-бафф на кастующем манекене (стенд для Purge/Spellsteal, DSP.2): Чародейский
    /// интеллект (1459, Dispel=1 Magic). Висит, пока не снимут/украдут.</summary>
    public const uint CasterDummyBuffSpellId = 1459;
    public const byte CasterDummyBuffDispelType = 1; // Magic

    /// <summary>Шаблон кастующего манекена. Faction 7 (нейтрал, атакуемый); кастует по игроку. INT.1.</summary>
    public static readonly CreatureTemplate CasterDummy = new(
        Entry: CasterDummyEntry,
        Name: "Кастующий манекен",
        SubName: "Уровень 80 — проверка прерывания",
        DisplayId: 3019,
        Level: 80,
        Faction: 7,    // нейтральный (жёлтый) — атакуемый
        UnitType: 7);  // Humanoid

    // ── Охотник (Ф2 #14): стреляет по игроку на расстоянии (физ. урон, эмуляция охотника). Урон НАНОСИТ.
    public const uint HunterDummyEntry = 990024;
    public const uint HunterDummyHealth = 50_000_000;
    public const uint HunterDummySpawnId = 9000024;
    public static readonly ulong HunterDummyGuid = UnitGuid(HunterDummyEntry, HunterDummySpawnId);
    public static bool IsHunterDummy(uint entry) => entry == HunterDummyEntry;
    public const uint HunterShotSpellId = 75;  // Auto Shot — визуал лога урона
    public const byte SchoolPhysical = 1;
    public static readonly CreatureTemplate HunterDummy = new(
        Entry: HunterDummyEntry, Name: "Манекен-охотник", SubName: "Уровень 80 — стрельба на расстоянии",
        DisplayId: 3019, Level: 80, Faction: 7, UnitType: 7);

    // ── Маг (Ф2 #14): кастующий манекен + доп. баффы (Стамина, метка друида) поверх Интеллекта —
    // стенд для Purge/Spellsteal/Dispel (3 снимаемых Magic-баффа).
    public const uint MageBuffStaminaSpellId = 1243; // Сила духа (Stamina), Dispel=1 Magic
    public const uint MageBuffMarkSpellId = 1126;    // Метка дикой природы (друид «лапка»), Dispel=1 Magic

    // ── Лечебный (Ф2 #14): отдельный манекен со СКРОМНЫМ HP (хилы заметны) + самослив быстрее регена,
    // старт 70%. Отдельный entry, чтобы не ломать M12-харнес (тот использует 990021 с большим HP).
    public const uint HealerDummyEntry = 990025;
    public const uint HealerDummyHealth = 50_000;
    public const uint HealerDummySpawnId = 9000025;
    public static readonly ulong HealerDummyGuid = UnitGuid(HealerDummyEntry, HealerDummySpawnId);
    public static bool IsHealerDummy(uint entry) => entry == HealerDummyEntry;
    public const float HealerSpawnPct = 0.7f;        // старт 70% HP
    public const uint HealerDrainPerSec = 1500;      // самослив/сек (первая версия — донастроим в игре)
    public static readonly CreatureTemplate HealerDummy = new(
        Entry: HealerDummyEntry, Name: "Лечебный манекен", SubName: "Уровень 80 — самослив, лечите его",
        DisplayId: 3019, Level: 80, Faction: 35, UnitType: 7); // дружелюбен — валидная цель хила

    // Реестр шаблонов для CREATURE_QUERY-фоллбэка: дев-манекены живут только в памяти (не в БД мира),
    // поэтому без записи здесь клиент не получает имя и рисует «Неизвестно». Ф2: регистрируем все.
    private static readonly Dictionary<uint, CreatureTemplate> ByEntry = new()
    {
        [TestDummy.Entry] = TestDummy,
        [TrainingDummy.Entry] = TrainingDummy,
        [HealDummy.Entry] = HealDummy,
        [AttackDummy.Entry] = AttackDummy,
        [CasterDummy.Entry] = CasterDummy,
        [HunterDummy.Entry] = HunterDummy,
        [HealerDummy.Entry] = HealerDummy,
    };

    public static CreatureTemplate? Find(uint entry)
        => ByEntry.TryGetValue(entry, out var template) ? template : null;
}
