using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Проки (Фаза 2 — PROC.1): шанс-триггер спелла на событии. Прок-аура (аура 42 PROC_TRIGGER_SPELL) несёт
/// триггер-спелл + procFlags (на каком событии) + procChance (% шанс). На событии перебираем активные
/// прок-ауры игрока с совпадающим флагом, ролим шанс и накладываем триггер-спелл (обычно бафф). Эталон —
/// CMaNGOS <c>Unit::ProcDamageAndSpell</c>. Крит-проки/ICD/procEx — вне scope PROC.1 (нужен спелл-крит).
/// </summary>
internal sealed class ProcService(SpellCatalog spellCatalog, AuraService auras)
{
    // PROC_FLAG_* (CMaNGOS SpellMgr.h) — события, которые мы умеем хукать.
    internal const uint ProcFlagDealMeleeSwing = 0x00000004;   // успешный мили-авто-удар
    internal const uint ProcFlagDealHarmfulSpell = 0x00010000; // успешный каст вредного спелла

    /// <summary>
    /// Событие <paramref name="procFlag"/> произошло: для каждой активной прок-ауры с этим флагом ролим
    /// procChance и накладываем триггер-спелл на себя. Несколько прок-аур могут сработать независимо.
    /// </summary>
    internal async Task TryProcAsync(WorldSession session, uint procFlag, CancellationToken ct)
    {
        if (session.Progression.Auras.Count == 0)
            return;

        foreach (var aura in session.Progression.Auras.ToList())
        {
            var info = await spellCatalog.GetAsync(aura.SpellId, ct);
            if (info is not { ProcTriggerSpellId: not 0 } || (info.ProcFlags & procFlag) == 0)
                continue;
            // procChance 0 — трактуем как «всегда»; иначе бросок d100.
            if (info.ProcChance > 0 && Random.Shared.Next(100) >= info.ProcChance)
                continue;

            var trig = await spellCatalog.GetAsync(info.ProcTriggerSpellId, ct);
            await auras.ApplyAsync(session, info.ProcTriggerSpellId, trig?.AuraDurationMs ?? 0,
                positive: true, form: 0, ct);
            session.Logger.LogDebug("PROC '{User}': {Aura} (шанс {Chance}%) → триггер {Trigger}",
                session.Account, aura.SpellId, info.ProcChance, info.ProcTriggerSpellId);
        }
    }
}
