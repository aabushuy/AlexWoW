namespace AlexWoW.WorldServer.Net.SessionState;

/// <summary>
/// Боевое состояние сессии: цель, авторитетный HP, мили-свинг, боевые ресурсы (ярость/энергия),
/// параметры оружия. Выделено из плоских полей <see cref="WorldSession"/>. M7 S9 #43.
/// </summary>
internal sealed class SessionCombatState
{
    /// <summary>Текущая цель (CMSG_SET_SELECTION). 0 — нет. M6.3.</summary>
    internal ulong SelectionGuid { get; set; }

    /// <summary>GUID существа, по которому идёт авто-атака (0 — не в бою). Читается тиком. M6.3.</summary>
    internal ulong CombatTargetGuid { get; set; }

    /// <summary>Время следующего мили-свинга (<see cref="Environment.TickCount64"/>, мс). M6.3.</summary>
    internal long NextMeleeSwingMs { get; set; }

    /// <summary>Послали ли клиенту «вне радиуса» для текущего эпизода (анти-спам). M6.3.</summary>
    internal bool MeleeNotInRangeNotified { get; set; }

    /// <summary>Авторитетное здоровье игрока (UNIT_FIELD_HEALTH). Меняется уроном существ. M6.7.</summary>
    internal uint Health { get; set; }
    internal uint MaxHealth { get; set; }

    /// <summary>Время последней боевой активности (нанёс/получил урон) — для внебоевого регена HP. M6.7.</summary>
    internal long LastCombatMs { get; set; }
    /// <summary>Время последнего тика регена HP (кадэнс 1 с). M6.7.</summary>
    internal long LastHealthRegenMs { get; set; }
    /// <summary>Игрок мёртв (HP=0, ждёт release/возрождения). M6.7.</summary>
    internal bool IsDead { get; set; }

    // --- Боевые ресурсы: ярость/энергия (M6.12) ---
    /// <summary>Ярость воина/друида (UNIT_FIELD_POWER1+1). Хранится ×10 (0..1000 = 0..100 у клиента).
    /// Копится от мили-урона, распадается вне боя. 0 у не-ярость-классов. M6.12.</summary>
    internal uint Rage { get; set; }
    /// <summary>Энергия разбойника (UNIT_FIELD_POWER1+3), 0..100. Реген ~постоянный. M6.12.</summary>
    internal uint Energy { get; set; }
    /// <summary>Сила рун DK (runic power, UNIT_FIELD_POWER1+6). Хранится ×10 (0..1000 = 0..100 у клиента).
    /// Копится от трат рун (RUNE.3), тратится RP-абилками (Frost Strike/Death Coil), распадается вне боя (RUNE.4).</summary>
    internal uint RunicPower { get; set; }
    /// <summary>Скорость оружия главной руки (мс) — для формулы ярости. Ставится в RefreshMeleeAsync. M6.12.</summary>
    internal uint MainHandSpeedMs { get; set; } = 2000;
    /// <summary>Урон оружия главной руки (min/max) — для мили-абилок (WEAPON_DAMAGE). RefreshMeleeAsync. M10.4a.</summary>
    internal float WeaponMinDamage { get; set; } = 1f;
    internal float WeaponMaxDamage { get; set; } = 2f;
    /// <summary>Надет ли щит (офф-хенд) — кэш из RefreshMeleeAsync для пересчёта блока при аурах («Блок щитом»).</summary>
    internal bool HasShield { get; set; }
    /// <summary>Защитные проценты и броня (кэш из RefreshMeleeAsync) — для обработки входящего удара (уклон/парри/блок/броня).</summary>
    internal float DodgePct { get; set; }
    internal float ParryPct { get; set; }
    internal float BlockPct { get; set; }
    internal uint ArmorValue { get; set; }
    /// <summary>Время последнего тика ресурса (реген энергии / распад ярости, кадэнс 1 с). M6.12.</summary>
    internal long LastResourceTickMs { get; set; }
    /// <summary>Sacred Shield (53601): время, когда прок поглощения снова доступен (ICD 6 с). ABS.3.</summary>
    internal long SacredShieldNextProcMs { get; set; }
    /// <summary>«На следующий замах» (MELEE.1): id абилки (Героический удар/Раскол/Свирепый удар), заместит
    /// следующую автоатаку; 0 — нет. Расходуется в <see cref="Handlers.PlayerMeleeService"/>.</summary>
    internal uint PendingNextSwingSpellId { get; set; }

    // --- Очки серии (combo points: рога/друид-кошка) — Фаза 2 (CP.1) ---
    /// <summary>Очки серии (0..5), накопленные на <see cref="ComboTargetGuid"/>. Генераторы копят,
    /// финишеры расходуют. Привязаны к конкретной цели: смена комбо-цели обнуляет.</summary>
    internal byte ComboPoints { get; set; }
    /// <summary>Цель, на которой накоплены очки серии (0 — нет). Меняется при касте генератора по новой цели.</summary>
    internal ulong ComboTargetGuid { get; set; }

    // --- Руны DK (Фаза 2, RUNE.1) ---
    /// <summary>6 рунных слотов DK (раскладка Blood,Blood,Unholy,Unholy,Frost,Frost — эталон mangos
    /// <c>runeSlotTypes</c>). Пусто у не-DK; инициализируется при входе в мир (<see cref="RuneType"/>/КД).</summary>
    internal RuneSlot[] Runes { get; set; } = [];
    /// <summary>Время последнего тика регена рун (мс, <see cref="Environment.TickCount64"/>). 0 — не инициализирован. RUNE.2.</summary>
    internal long LastRuneTickMs { get; set; }

    /// <summary>Сброс при выходе из мира — только то, что сбрасывалось в LeaveWorld и раньше
    /// (HP/ресурсы/тайминги переживают выход by design — переинициализируются при входе).</summary>
    internal void Reset()
    {
        CombatTargetGuid = 0; // M6.3: вне мира боя нет
        SelectionGuid = 0;
        IsDead = false;       // M6.7: боевое/жизненное состояние сбрасывается при выходе
        ComboPoints = 0;      // CP.1: очки серии не переживают выход из мира
        ComboTargetGuid = 0;
        Runes = [];           // RUNE.1: руны переинициализируются при входе в мир
    }
}

/// <summary>Тип руны DK (эталон mangos <c>RuneType</c>). Значения совпадают с клиентскими (рисует цвет руны).</summary>
internal enum RuneType : byte
{
    Blood = 0,
    Unholy = 1,
    Frost = 2,
    Death = 3,
}

/// <summary>
/// Один рунный слот DK (RUNE.1): базовый тип, текущий тип (отличается от базового при death-конвертации,
/// RUNE.5) и остаток кулдауна восстановления (мс; 0 — руна доступна). Аналог mangos <c>RuneInfo</c>.
/// </summary>
internal struct RuneSlot
{
    /// <summary>Базовый (исходный) тип слота — задаётся раскладкой при инициализации, не меняется.</summary>
    internal RuneType BaseType;
    /// <summary>Текущий тип (= базовому, пока не сконвертирован в Death). Им красится руна и резолвится стоимость.</summary>
    internal RuneType CurrentType;
    /// <summary>Остаток кулдауна восстановления (мс). 0 — руна готова к трате. Тикается в WorldTick (RUNE.2).</summary>
    internal int CooldownMs;

    /// <summary>Руна доступна к трате (кулдаун истёк).</summary>
    internal readonly bool Ready => CooldownMs <= 0;
}
