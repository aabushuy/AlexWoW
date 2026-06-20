using AlexWoW.WorldServer.Net;

namespace AlexWoW.WorldServer.Handlers.Spells;

/// <summary>
/// Обработчик скриптовой ауры (SPELL_AURA_DUMMY=4 / OVERRIDE_CLASS_SCRIPTS=112) — per-spellId логика
/// талантов и именных абилок (Ignite, Clearcasting, Vigilance, Earth Shield и т.п.). Эталон —
/// CMaNGOS <c>Aura::HandleAuraDummy</c> + <c>Unit::HandleDummyAuraProc</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="OnApplyAsync"/> — наложение скриптовой ауры: побочные эффекты сверх стандартной иконки.</item>
/// <item><see cref="OnRemoveAsync"/> — снятие (истечение/cancel/unlearn): откат побочных эффектов apply.</item>
/// <item><see cref="OnProcAsync"/> — наша аура «зарегистрирована как прок-источник» и сработала.
/// true — стандартный <c>TryProc</c> прерывается для этой ауры (custom-обработчик «съел» событие).</item>
/// </list>
/// Реализации обычно подписываются на 1-2 хука из 3 — наследуй <see cref="DummyAuraHandlerBase"/>.
/// Сам обработчик инжектит нужные зависимости (SpellCatalog/AuraService/…) через DI.
/// </remarks>
internal interface IDummyAuraHandler
{
    /// <summary>Аура наложена.</summary>
    Task OnApplyAsync(WorldSession session, uint spellId, CancellationToken ct);

    /// <summary>Аура снята (истечение / cancel / unlearn).</summary>
    Task OnRemoveAsync(WorldSession session, uint spellId, CancellationToken ct);

    /// <summary>Аура «дёрнула» прок. true — стандартный generic-триггер пропустить для этой ауры.</summary>
    Task<bool> OnProcAsync(WorldSession session, uint spellId, DummyProcContext ctx, CancellationToken ct);
}

/// <summary>Контекст одного прока для DUMMY-обработчика — параметры <see cref="ProcService.TryProcAsync"/>.</summary>
internal readonly record struct DummyProcContext(
    ProcFlag ProcFlag,
    ProcFlagEx ProcEx,
    byte SpellSchoolMask,
    uint SourceSpellId,
    uint WeaponAttackSpeedMs);

/// <summary>Базовый «no-op» — обычно обработчику нужен 1-2 хука из 3.</summary>
internal abstract class DummyAuraHandlerBase : IDummyAuraHandler
{
    public virtual Task OnApplyAsync(WorldSession session, uint spellId, CancellationToken ct)
        => Task.CompletedTask;
    public virtual Task OnRemoveAsync(WorldSession session, uint spellId, CancellationToken ct)
        => Task.CompletedTask;
    public virtual Task<bool> OnProcAsync(WorldSession session, uint spellId, DummyProcContext ctx, CancellationToken ct)
        => Task.FromResult(false);
}
