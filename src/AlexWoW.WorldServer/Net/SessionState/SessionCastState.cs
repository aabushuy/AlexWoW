namespace AlexWoW.WorldServer.Net.SessionState;

/// <summary>
/// Состояние каста сессии: текущий каст, GCD, мана и её реген, кулдауны спеллов.
/// Выделено из плоских полей <see cref="WorldSession"/>. M7 S9 #43.
/// </summary>
internal sealed class SessionCastState
{
    // --- Каст спелла (M6.4) ---
    /// <summary>Спелл, который сейчас кастуется (0 — нет каста). Завершается в тике.</summary>
    internal uint CastingSpellId { get; set; }
    // cast_count и target теперь протаскиваются параметрами каста (не через сессию) — повторное нажатие
    // во время каста не перезатирает счётчик завершаемого каста (M10.4a фикс залипания завершения).
    /// <summary>Позиция в начале каста — для прерывания при сдвиге (движение, не поворот). M6.4.</summary>
    internal float CastStartX { get; set; }
    internal float CastStartY { get; set; }
    /// <summary>Поколение каста: инкремент на каждый каст; отложенное завершение проверяет совпадение
    /// (чтобы не завершить отменённый/перебитый каст). M6.4.</summary>
    internal int CastGeneration { get; set; }
    /// <summary>Момент окончания глобального кулдауна (GCD, <see cref="Environment.TickCount64"/>, мс). M10.3.</summary>
    internal long GcdEndMs { get; set; }

    /// <summary>Текущая/макс. мана (UNIT_FIELD_POWER1). MaxMana=0 — класс без маны (rage/energy):
    /// расход не применяется. Инициализируется при входе в мир. M6.4 инкремент 2.</summary>
    internal uint Mana { get; set; }
    internal uint MaxMana { get; set; }
    /// <summary>Базовый MaxMana (без аур-бонуса от Intellect MOD_STAT) — для пересчёта в PeriodicsService.</summary>
    internal uint BaseMaxMana { get; set; }
    /// <summary>Время последнего успешного каста — «правило 5 секунд» (реген маны паузится). M6.4.</summary>
    internal long LastSpellCastMs { get; set; }
    /// <summary>Время последнего тика регена маны (кадэнс 1 с). M6.4.</summary>
    internal long LastManaRegenMs { get; set; }

    /// <summary>Кулдауны спеллов: spellId → момент готовности (<see cref="Environment.TickCount64"/>, мс). M6.4.</summary>
    internal System.Collections.Generic.Dictionary<uint, long> SpellCooldowns { get; } = [];

    /// <summary>Шанс крита заклинаний в % (CRIT.1). База 0 — крит из статов (интеллект/крит-рейтинг) пока не
    /// моделируется, и 0 не зашумляет Spell QA (сверка с min/max). Включается дев-командой <c>.setcrit</c>.</summary>
    internal int SpellCritChance { get; set; }

    /// <summary>Ф2 #2: сила заклинаний (плоский бонус к школьному урону) — session-оверрайд dev-редактора.
    /// Прибавляется к урону заклинаний (SpellEffectsService.ComputeDamage) и пушится в
    /// PLAYER_FIELD_MOD_DAMAGE_DONE_POS (лист «Заклинания → Сила заклинаний»).</summary>
    internal uint SpellPower { get; set; }

    /// <summary>Сброс при выходе из мира — только то, что сбрасывалось в LeaveWorld и раньше
    /// (мана/GCD/тайминги переживают выход by design — переинициализируются при входе).</summary>
    internal void Reset()
    {
        CastingSpellId = 0;   // M6.4: каст прерывается при выходе
        SpellCooldowns.Clear();
    }
}
