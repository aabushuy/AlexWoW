using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Спеллы-переключатели (M6.12/M7 #21, DI-сервис M7 S3 — бывший статик SpellToggles): стойки воина /
/// ауры паладина / аспекты охотника — мгновенный каст без маны/цели/кулдауна → перманентная аура (персист),
/// эксклюзивная в группе. Делегирует наложение в <see cref="AuraService"/>; отделено от обычного каста
/// (<see cref="SpellCastService"/>) по SRP. GO шлёт <see cref="SpellGoSender"/> (разрыв цикла с кастом).
/// </summary>
internal sealed class SpellTogglesService(SpellGoSender spellGo, AuraService auras, SpellCatalog spellCatalog,
    CombatResourcesService combatResources)
{
    /// <summary>
    /// Если spell — переключатель: завершить каст у клиента (SPELL_GO) и наложить перманентную ауру-форму.
    /// Возвращает true, если spell был переключателем и обработан (обычный каст пропускаем).
    /// </summary>
    internal async Task<bool> TryToggleAsync(WorldSession session, uint spellId, byte castCount, CancellationToken ct)
    {
        if (!SpellCatalog.TryGetToggle(spellId, out var toggle))
            return false;

        await spellGo.SendSpellGoAsync(session, spellId, targetGuid: 0, castCount, ct); // завершить каст у клиента

        // Повторный каст ОТМЕНЯЕМОЙ формы (Shadowform/Stealth/Ghost Wolf) — выход из неё (как клик по кнопке
        // формы на оффе): снимаем активную ауру той же формы (сбрасывает байт формы → кнопка «отжимается»).
        if (toggle.Cancelable && toggle.Form != 0
            && session.Progression.Auras.FirstOrDefault(a => a.ShapeshiftForm == toggle.Form) is { } active)
        {
            await auras.RemoveAsync(session, active.SpellId, ct);
            // §1 Формы друида: выход из формы → вернуть тип ресурса (мана) по текущей (уже сброшенной) форме.
            await combatResources.ApplyFormPowerAsync(session, session.Progression.ShapeshiftForm, ct);
            session.Logger.LogDebug("TOGGLE-OFF '{User}': spell={Spell} форма={Form}", session.Account, active.SpellId, toggle.Form);
            return true;
        }

        // Стат-эффект формы-переключателя (Shadowform +15% Shadow): несём % урона по школе на ауре.
        var info = await spellCatalog.GetAsync(spellId, ct);
        await auras.ApplyAsync(session, spellId, durationMs: 0, positive: true, toggle.Form, ct,
            group: toggle.Group, persist: true,
            damageDonePct: info?.DamageDonePct ?? 0, damageDoneSchool: info?.DamageDoneSchoolMask ?? 0,
            damageTakenPct: info?.DamageTakenPct ?? 0);
        // §1 Формы друида: вход в форму → сменить тип ресурса (медведь→ярость, кошка→энергия, прочее→мана).
        await combatResources.ApplyFormPowerAsync(session, toggle.Form, ct);
        session.Logger.LogDebug("TOGGLE '{User}': spell={Spell} форма={Form} группа={Group}",
            session.Account, spellId, toggle.Form, toggle.Group);
        return true;
    }
}
