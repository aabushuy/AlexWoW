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

    private const uint ProcExCriticalHit = 0x02; // spell_proc_event.procEx: прок только на крит триггера (PROC.2)

    /// <summary>
    /// Событие <paramref name="procFlag"/> произошло: для каждой активной прок-ауры с этим флагом ролим
    /// procChance и накладываем триггер-спелл. <paramref name="wasCrit"/>/<paramref name="spellSchoolMask"/> —
    /// крит/школа триггера: для крит-проков (spell_proc_event.procEx PROC_EX_CRITICAL_HIT) и фильтра по школе (PROC.2).
    /// </summary>
    internal async Task TryProcAsync(WorldSession session, uint procFlag, CancellationToken ct,
        bool wasCrit = false, byte spellSchoolMask = 0)
    {
        if (session.Progression.Auras.Count == 0)
            return;

        foreach (var aura in session.Progression.Auras.ToList())
        {
            // §8 Печати паладина — on-hit прок обрабатывает SealService (по свингу), НЕ дублируем generic-проком:
            // иначе триггер-спелл печати наложился бы на САМОГО паладина как само-бафф (Печать справедливости →
            // оглушение себя; Печать мудрости/света → дубль-аура «второй печатью», ломающая эксклюзивность). §8 фикс.
            if (SpellCatalog.ExclusiveAuraGroup(aura.SpellId) == SpellCatalog.GroupPaladinSeal)
                continue;

            var info = await spellCatalog.GetAsync(aura.SpellId, ct);
            if (info is not { ProcTriggerSpellId: not 0 })
                continue;

            // PROC.2: spell_proc_event уточняет события/условия. procFlags оттуда (если заданы) переопределяют
            // шаблон; procEx PROC_EX_CRITICAL_HIT требует крит; SchoolMask ограничивает школу триггера.
            var ev = await spellCatalog.GetProcEventAsync(aura.SpellId, ct);
            var effectiveFlags = ev is { ProcFlags: not 0 } ? ev.ProcFlags : info.ProcFlags;
            if ((effectiveFlags & procFlag) == 0)
                continue;
            if (ev is not null && (ev.ProcEx & ProcExCriticalHit) != 0 && !wasCrit)
                continue; // крит-прок, а триггер не кританул
            if (ev is { SchoolMask: not 0 } && (ev.SchoolMask & spellSchoolMask) == 0)
                continue; // школа триггера не подходит

            // procChance 0 — трактуем как «всегда»; иначе бросок d100.
            if (info.ProcChance > 0 && Random.Shared.Next(100) >= info.ProcChance)
                continue;

            var trig = await spellCatalog.GetAsync(info.ProcTriggerSpellId, ct);
            await auras.ApplyAsync(session, info.ProcTriggerSpellId, trig?.AuraDurationMs ?? 0,
                positive: true, form: 0, ct);
            session.Logger.LogDebug("PROC '{User}': {Aura} (шанс {Chance}%, крит={Crit}) → триггер {Trigger}",
                session.Account, aura.SpellId, info.ProcChance, wasCrit, info.ProcTriggerSpellId);
        }
    }
}
