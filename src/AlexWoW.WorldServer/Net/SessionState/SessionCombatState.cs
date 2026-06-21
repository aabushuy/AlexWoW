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
    /// <summary>§1 Формы друида: тип ресурса активной формы (0 — класс по умолчанию/мана; 1 — ярость медведя;
    /// 3 — энергия кошки). Делает реген/трату ресурса форм-зависимыми для друида (класс=мана). Сброс при выходе.</summary>
    internal byte FormPowerType { get; set; }

    /// <summary>§2 Осколки души: guid существа, на которое ЧК скастовал Drain Soul. При убийстве этого существа
    /// игрок получает осколок души (item 6265). 0 — метки нет. Перезаписывается новым кастом Drain Soul.</summary>
    internal ulong DrainSoulTargetGuid { get; set; }
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
    /// <summary>Шанс мили-крита (%) из статов (кэш RefreshMeleeAsync) — ролл крита автоатаки/мили-абилки. CRIT.2.</summary>
    internal float MeleeCritPct { get; set; }
    /// <summary>Ф2 #1: рейтинг меткости (combat rating, очки) — session-оверрайд dev-редактора. Конвертируется
    /// в % снижения промаха автоатаки/мили-абилок (CombatRatingConversion) и пушится в
    /// PLAYER_FIELD_COMBAT_RATING_1[HitMelee] для отображения «Рейтинг меткости» в листе персонажа.</summary>
    internal uint BaseMeleeHitRating { get; set; }
    /// <summary>Ф2 #2: мастерство (очки expertise) — session-оверрайд. Снижает dodge/parry цели на 0.25%/очко
    /// (OutgoingMeleeResolver). Пушится в PLAYER_EXPERTISE/PLAYER_OFFHAND_EXPERTISE (лист «Мастерство»).</summary>
    internal uint BaseExpertise { get; set; }
    /// <summary>Ф2 #2: защита (бонус-очки навыка) — session-оверрайд. Снижает шанс быть раскритованным существом
    /// на 0.04%/очко (CreatureCombatAI). Пушится как CR_DEFENSE_SKILL рейтинг (лист «Защита»).</summary>
    internal uint BaseDefenseSkill { get; set; }
    /// <summary>Ф2 #2: устойчивость (resilience rating) — session-оверрайд. Снижает бонус крит-урона существа по
    /// игроку (CombatRatingConversion → %). Пушится в CR_CRIT_TAKEN_SPELL (лист «Устойчивость»).</summary>
    internal uint BaseResilienceRating { get; set; }
    internal uint ArmorValue { get; set; }
    /// <summary>Базовая сила атаки (мили) из статов/класса — кэш RefreshMeleeAsync. Используется в формуле
    /// автоатаки игрока и при отдельной пересылке поля UNIT_FIELD_ATTACK_POWER (PeriodicsService.SendAttackPowerAsync).</summary>
    internal uint BaseMeleeAttackPower { get; set; }
    /// <summary>Базовая сила атаки (дальний бой) — кэш RefreshMeleeAsync (значима для охотника).</summary>
    internal uint BaseRangedAttackPower { get; set; }
    /// <summary>Сумма аур-бонусов мили-AP (Боевой клич / Благословение Могущества) — обновляется в
    /// PeriodicsService при apply/remove ауры. Учитывается в формуле автоатаки.</summary>
    internal int AttackPowerBonus { get; set; }
    /// <summary>Сумма аур-бонусов AP дальнего боя (вторая аура Боевого клича) — для охотника.</summary>
    internal int RangedAttackPowerBonus { get; set; }
    /// <summary>База статов (Сила/Ловкость/Выносливость/Интеллект/Дух) из StatsStore — кэш RecalcStatsAsync.
    /// PeriodicsService.SendStatsAsync суммирует базу с активными аурами MOD_STAT (29) и шлёт UnitStat0..4.</summary>
    internal uint BaseStr { get; set; }
    internal uint BaseAgi { get; set; }
    internal uint BaseSta { get; set; }
    internal uint BaseInt { get; set; }
    internal uint BaseSpi { get; set; }
    /// <summary>Базовый MaxHealth (без аур-бонуса от Stamina) — для пересчёта в SendStatsAsync.</summary>
    internal uint BaseMaxHealth { get; set; }
    /// <summary>Время последнего тика ресурса (реген энергии / распад ярости, кадэнс 1 с). M6.12.</summary>
    internal long LastResourceTickMs { get; set; }
    /// <summary>Sacred Shield (53601): время, когда прок поглощения снова доступен (ICD 6 с). ABS.3.</summary>
    internal long SacredShieldNextProcMs { get; set; }

    /// <summary>SPELL.T3 Warrior Victory Rush (34428): время последнего убийства существа игроком (мс,
    /// <see cref="System.Environment.TickCount64"/>). Окно Victory Rush — 20с (CMaNGOS), вне окна каст отказан.
    /// Ставится KillRewardService.OnCreatureKilledAsync; 0 — ещё никого не убивали.</summary>
    internal long LastKillMs { get; set; }

    /// <summary>SPELL.T3 Warrior Victory Rush: момент истечения AURA_STATE_WARRIOR_VICTORY_RUSH (бит 6 в
    /// UNIT_FIELD_AURASTATE, значение state=7 — общий с HUNTER_PARRY, но триггер у воина другой: после kill).
    /// Ставится KillRewardService для класса воина на 20с. 0 — state не активен.</summary>
    internal long VictoryRushStateExpiresMs { get; set; }

    /// <summary>#3797 DK Rune Strike (56815/56816): окно 5с после успешного dodge/parry игроком (только
    /// класс DK=6). Чисто серверный гейт — клиентский AURA_STATE для Rune Strike не задействован (кнопка
    /// активна всегда после изучения, попытка каста вне окна → SPELL_FAILED_CASTER_AURASTATE).</summary>
    internal long RuneStrikeWindowExpiresMs { get; set; }

    /// <summary>#3797 Warrior Overpower (7384): окно 5с после dodged мили-удара игроком-воином (его автоатака
    /// уклонена целью). Только класс Warrior=1. Чисто серверный гейт.</summary>
    internal long OverpowerWindowExpiresMs { get; set; }

    /// <summary>DEFENSE.1: момент истечения AURA_STATE_DEFENSE (мс). Ставится на 5с при успешном
    /// dodge/parry/block игрока (CreatureCombatAI ApplyResolveOutcome). Гейт каста Revenge:
    /// SpellCastService отказывает, если spell.CasterAuraState=1 и now &gt;= DefenseStateExpiresMs.
    /// 0 — state не активен. После успешного каста Revenge — снимается AuraStateService.</summary>
    internal long DefenseStateExpiresMs { get; set; }

    /// <summary>DEFENSE.2: момент истечения AURA_STATE_HUNTER_PARRY (мс). Бит 6 в UNIT_FIELD_AURASTATE
    /// (значение state=7 у клиента/DBC — общее с WARRIOR_VICTORY_RUSH, но для разных классов разные
    /// триггеры). Ставится Hunter'у на 5с при успешном parry; гейт каста Counterattack.</summary>
    internal long HunterParryStateExpiresMs { get; set; }
    /// <summary>«На следующий замах» (MELEE.1): id абилки (Героический удар/Раскол/Свирепый удар), заместит
    /// следующую автоатаку; 0 — нет. Расходуется в <see cref="Handlers.PlayerMeleeService"/>.</summary>
    internal uint PendingNextSwingSpellId { get; set; }
    /// <summary>cast_count исходного каста on-next-swing абилки — для SMSG_SPELL_GO на самом замахе
    /// (клиент сопоставляет с pending-кастом и снимает подсветку кнопки). MELEE.1.</summary>
    internal byte PendingNextSwingCastCount { get; set; }

    /// <summary>§8 Активный яд на оружии (нанесён энчантом, НЕ баффом): spellId апплай-яда; 0 — нет.
    /// On-hit прок читает его (PoisonService); свечение оружия — visible-item enchant. Эксклюзив — один яд.</summary>
    internal uint ActivePoisonSpellId { get; set; }
    /// <summary>Момент истечения активного яда (мс). По нему снимаем свечение оружия и сбрасываем яд.</summary>
    internal long ActivePoisonExpiresMs { get; set; }

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
        DefenseStateExpiresMs = 0; // DEFENSE.1: окно Revenge не переживает выход
        HunterParryStateExpiresMs = 0; // DEFENSE.2: окно Counterattack не переживает выход
        VictoryRushStateExpiresMs = 0; // SPELL.T3: окно Victory Rush не переживает выход
        RuneStrikeWindowExpiresMs = 0; // #3797: окно Rune Strike не переживает выход
        OverpowerWindowExpiresMs = 0;  // #3797: окно Overpower не переживает выход
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
