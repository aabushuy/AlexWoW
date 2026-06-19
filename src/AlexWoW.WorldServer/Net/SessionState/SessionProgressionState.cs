namespace AlexWoW.WorldServer.Net.SessionState;

/// <summary>
/// Прогрессия персонажа в сессии: опыт, спеллы, таланты, навыки/профессии, ауры и периодики.
/// Выделено из плоских полей <see cref="WorldSession"/>. M7 S9 #43.
/// </summary>
internal sealed class SessionProgressionState
{
    /// <summary>Текущий опыт на уровне (PLAYER_XP). Прокачка M9.1.</summary>
    internal uint Xp { get; set; }

    /// <summary>Известные игроку спеллы (стартовые по классу + языковые + изученные у тренера). Для
    /// HasSpell-проверок тренера и анти-дубля. Загружается при входе в мир. M9.3.</summary>
    internal HashSet<uint> KnownSpells { get; } = [];

    /// <summary>Свободные очки талантов (PLAYER_CHARACTER_POINTS1). Вычисляются: MaxPoints(level) − потрачено. M9.6.</summary>
    internal uint TalentPoints { get; set; }
    /// <summary>Изученные таланты: talentId → ранг (0-индексный). Загружается при входе. M9.6/M9.7.</summary>
    internal Dictionary<uint, byte> LearnedTalents { get; } = [];

    /// <summary>Книга навыков персонажа (профессии и пр.). Загружается при входе в мир. M11.1.</summary>
    internal World.PlayerSkillBook SkillBook { get; } = new();

    /// <summary>Текущий тир-спелл каждой профессии в книге: skillId → (spell, потолок). Высший тир
    /// supercede'ит низшие (в книге показывается один). Заполняется при входе/изучении. M11.</summary>
    internal Dictionary<ushort, (uint Spell, ushort Max)> ProfessionRankSpell { get; } = [];

    /// <summary>Активные модификаторы спеллов (пассивные таланты с аурами 107/108): стоимость/урон/КД
    /// абилок. Перестраивается при входе, дополняется при изучении (SpellModifierService). M10.6.</summary>
    internal List<World.SpellModifier> SpellMods { get; } = [];

    /// <summary>Последние отправленные клиенту итоги модификаторов (SMSG_SET_FLAT/PCT_SPELL_MODIFIER):
    /// (бит маски, op, процентный?) → значение. Для диффа при изучении/сбросе (исчезнувшие зануляются). M10.6.</summary>
    internal Dictionary<(byte Eff, byte Op, bool Pct), int> SentSpellModTotals { get; } = [];

    /// <summary>Активные ауры (баффы/дебаффы/формы). Слот = позиция в баф-баре. M6.11.</summary>
    internal List<World.ActiveAura> Auras { get; } = [];
    /// <summary>Активные периодические эффекты этого кастера (DoT на существах / HoT на себе). M10.4b.</summary>
    internal List<Handlers.PeriodicEffect> Periodics { get; } = [];
    /// <summary>Текущая форма шейпшифта (стойка воина/форма друида); 0 — нет формы. UNIT_FIELD_BYTES_2 байт 3. M6.11.</summary>
    internal byte ShapeshiftForm { get; set; }

    /// <summary>ICD (skin cooldown) прок-аур: spellId прок-ауры → Environment.TickCount64 момента последнего срабатывания.
    /// CMaNGOS spell_proc_event.cooldown — блокирует повторный прок до истечения cooldown_ms. PROC.T3.</summary>
    internal Dictionary<uint, long> ProcLastFiredMs { get; } = [];

    /// <summary>Сброс при выходе из мира — только то, что сбрасывалось в LeaveWorld и раньше
    /// (Xp/TalentPoints переживают выход by design — переинициализируются при входе).</summary>
    internal void Reset()
    {
        KnownSpells.Clear();  // M9.3: набор спеллов перезагружаем при следующем входе
        LearnedTalents.Clear(); // M9.6: таланты перезагружаем при следующем входе
        SkillBook.Clear();    // M11.1: навыки перезагружаем при следующем входе
        ProfessionRankSpell.Clear();
        SpellMods.Clear();    // M10.6: модификаторы пересобираются при следующем входе
        SentSpellModTotals.Clear();
        Auras.Clear();        // M6.11: ауры сбрасываются при выходе (клиент пересоздаст при входе)
        Periodics.Clear();    // M10.4b: периодические эффекты (DoT/HoT)
        ShapeshiftForm = 0;
        ProcLastFiredMs.Clear(); // PROC.T3: ICD прок-аур
    }
}
