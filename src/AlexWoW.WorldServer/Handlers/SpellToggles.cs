using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Спеллы-переключатели (M6.12/M7 #21): стойки воина / ауры паладина / аспекты охотника — мгновенный
/// каст без маны/цели/кулдауна → перманентная аура (персист), эксклюзивная в группе. Делегирует наложение
/// в <see cref="Auras"/>; отделено от обычного каста (<see cref="SpellCaster"/>) по SRP.
/// </summary>
public static class SpellToggles
{
    /// <summary>
    /// Если spell — переключатель: завершить каст у клиента (SPELL_GO) и наложить перманентную ауру-форму.
    /// Возвращает true, если spell был переключателем и обработан (обычный каст пропускаем).
    /// </summary>
    internal static async Task<bool> TryToggleAsync(WorldSession session, uint spellId, CancellationToken ct)
    {
        if (!SpellCatalog.TryGetToggle(spellId, out var toggle))
            return false;

        await SpellCaster.SendSpellGoAsync(session, spellId, targetGuid: 0, ct); // завершить каст у клиента
        await Auras.ApplyAsync(session, spellId, durationMs: 0, positive: true, toggle.Form, ct,
            group: toggle.Group, persist: true);
        session.Logger.LogDebug("TOGGLE '{User}': spell={Spell} форма={Form} группа={Group}",
            session.Account, spellId, toggle.Form, toggle.Group);
        return true;
    }
}
