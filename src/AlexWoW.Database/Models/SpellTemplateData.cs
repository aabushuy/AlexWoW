namespace AlexWoW.Database.Models;

/// <summary>
/// Строка <c>spell_template</c> (дамп Spell.dbc, БД mangos) — поля, нужные серверу для каста (M10.2):
/// школа (маска), индекс времени каста, тип ресурса/стоимость маны, кулдаун, и до 3 эффектов
/// (тип + база/разброс величины) для определения урона/хила. Парсер DBC не нужен — всё в дампе (#27).
/// Имена свойств совпадают с колонками таблицы (Dapper маппит по именам; стиль <c>{ get; init; }</c> —
/// как у прочих Dapper-DTO проекта; позиционный record Dapper маппит через конструктор и ненадёжен).
/// </summary>
public sealed record SpellTemplateData
{
    public uint Id { get; init; }
    public uint SchoolMask { get; init; }
    public uint CastingTimeIndex { get; init; }
    public int PowerType { get; init; }
    public uint ManaCost { get; init; }
    public uint ManaCostPercentage { get; init; }
    public uint RecoveryTime { get; init; }
    public uint CategoryRecoveryTime { get; init; }
    /// <summary>Время глобального кулдауна (GCD) в мс — обычно 1500. M10.3.</summary>
    public uint StartRecoveryTime { get; init; }
    /// <summary>Индекс длительности ауры → SpellDuration.dbc (для DoT/HoT). M10.4b.</summary>
    public uint DurationIndex { get; init; }
    public int Effect1 { get; init; }
    public int Effect2 { get; init; }
    public int Effect3 { get; init; }
    public int EffectBasePoints1 { get; init; }
    public int EffectBasePoints2 { get; init; }
    public int EffectBasePoints3 { get; init; }
    public int EffectDieSides1 { get; init; }
    public int EffectDieSides2 { get; init; }
    public int EffectDieSides3 { get; init; }
    /// <summary>Тип ауры эффекта (SPELL_AURA_*): 3=PERIODIC_DAMAGE, 8=PERIODIC_HEAL. M10.4b.</summary>
    public int EffectApplyAuraName1 { get; init; }
    public int EffectApplyAuraName2 { get; init; }
    public int EffectApplyAuraName3 { get; init; }
    /// <summary>Интервал тика периодической ауры (мс). M10.4b.</summary>
    public int EffectAmplitude1 { get; init; }
    public int EffectAmplitude2 { get; init; }
    public int EffectAmplitude3 { get; init; }
}
