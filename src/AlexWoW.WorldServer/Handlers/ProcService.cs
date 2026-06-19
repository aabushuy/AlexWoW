// Порт CMaNGOS-WoTLK: src/game/Spells/UnitAuraProcHandler.cpp (Unit::ProcDamageAndSpell, ~4372 строки)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Spells/UnitAuraProcHandler.cpp. GPL-2.0.

using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Проки (Фаза 2 — PROC.1, в работе на T1 порта CMaNGOS): шанс-триггер спелла на событии.
/// Прок-аура (аура 42 PROC_TRIGGER_SPELL) несёт триггер-спелл + procFlags (на каком событии) + procChance.
/// На событии перебираем активные прок-ауры игрока с совпадающим флагом, ролим шанс и накладываем
/// триггер-спелл (обычно бафф). Эталон поведения — CMaNGOS Unit::ProcDamageAndSpell.
/// </summary>
/// <remarks>
/// Не реализовано (отдельные таски эпика «Порт CMaNGOS» в канбане):
/// T2 — полный enum PROC_EX (22 бита: hit/crit/miss/dodge/parry/block/...);
/// T3 — ICD (spell_proc_event.cooldown) на прок-ауру;
/// T4 — PPM (proc-per-minute) для weapon-based проков;
/// T5 — spellFamilyName/spellFamilyMask фильтр триггеранта.
/// </remarks>
internal sealed class ProcService(SpellCatalog spellCatalog, AuraService auras)
{
    // procEx-биты, которые уже умеем фильтровать (полный enum — T2).
    private const uint ProcExCriticalHit = 0x02; // spell_proc_event.procEx: прок только на крит триггера

    /// <summary>
    /// Событие <paramref name="procFlag"/> произошло на сессии <paramref name="session"/>.
    /// Для каждой активной прок-ауры с этим флагом ролим procChance и накладываем триггер-спелл.
    /// <paramref name="wasCrit"/>/<paramref name="spellSchoolMask"/> — крит/школа триггеранта:
    /// для крит-проков (procEx PROC_EX_CRITICAL_HIT) и фильтра по школе.
    /// </summary>
    /// <remarks>
    /// <paramref name="session"/> = сторона, на чьих аурах смотрим. Для <c>Deal*</c>-флагов это
    /// атакующий (его ауры типа Sudden Death, Sword and Board); для <c>Take*</c> — жертва
    /// (Natural Reaction, Blade Barrier).
    /// </remarks>
    internal async Task TryProcAsync(WorldSession session, ProcFlag procFlag, CancellationToken ct,
        bool wasCrit = false, byte spellSchoolMask = 0)
    {
        if (session.Progression.Auras.Count == 0)
            return;

        foreach (var aura in session.Progression.Auras.ToList())
        {
            // §8 Печати паладина — on-hit прок обрабатывает SealService (по свингу), НЕ дублируем generic-проком:
            // иначе триггер-спелл печати наложился бы на САМОГО паладина как само-бафф (Печать справедливости →
            // оглушение себя; Печать мудрости/света → дубль-аура «второй печатью», ломающая эксклюзивность).
            if (SpellCatalog.ExclusiveAuraGroup(aura.SpellId) == SpellCatalog.GroupPaladinSeal)
                continue;

            var info = await spellCatalog.GetAsync(aura.SpellId, ct);
            if (info is not { ProcTriggerSpellId: not 0 })
                continue;

            // spell_proc_event уточняет события/условия. procFlags оттуда (если заданы) переопределяют
            // шаблон; procEx PROC_EX_CRITICAL_HIT требует крит; SchoolMask ограничивает школу триггера.
            var ev = await spellCatalog.GetProcEventAsync(aura.SpellId, ct);
            var effectiveFlags = (ProcFlag)(ev is { ProcFlags: not 0 } ? ev.ProcFlags : info.ProcFlags);
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
            session.Logger.LogDebug("PROC '{User}': {Aura} ({Flag}, шанс {Chance}%, крит={Crit}) → триггер {Trigger}",
                session.Account, aura.SpellId, procFlag, info.ProcChance, wasCrit, info.ProcTriggerSpellId);
        }
    }
}
