namespace AlexWoW.Database.Models;

/// <summary>Режим сессии захвата проверки заклинаний (M12 Spell QA).</summary>
public enum SpellTestMode : byte
{
    /// <summary>Ручной: тестировщик сам кастует по манекенам (.spelltest start/stop).</summary>
    Manual = 0,

    /// <summary>Авто-харнесс: сервер прогоняет все абилки класса (.spelltest run).</summary>
    Harness = 1,
}

/// <summary>Тип записанного эффекта в строке результата (M12 Spell QA).</summary>
public enum SpellTestResultType : byte
{
    DirectDamage = 0,
    DirectHeal = 1,
    DotTick = 2,
    HotTick = 3,
}
