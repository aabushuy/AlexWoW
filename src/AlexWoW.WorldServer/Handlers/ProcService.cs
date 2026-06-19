// Порт CMaNGOS-WoTLK: src/game/Spells/UnitAuraProcHandler.cpp (Unit::ProcDamageAndSpell, ~4372 строки)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Spells/UnitAuraProcHandler.cpp. GPL-2.0.

using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Проки: шанс-триггер спелла на событии. Прок-аура (аура 42 PROC_TRIGGER_SPELL) несёт триггер-спелл +
/// procFlags (на каком событии) + procChance. На событии перебираем активные прок-ауры игрока с совпадающим
/// флагом, ролим шанс и накладываем триггер-спелл. Эталон — CMaNGOS Unit::ProcDamageAndSpell.
/// </summary>
/// <remarks>
/// Что покрыто (порт CMaNGOS):
/// T1 ✅ — полный enum ProcFlag (25 битов), MainHand/OffHand, victim-side TakeMeleeSwing/TakeAnyDamage, Kill.
/// T2 ✅ — enum ProcFlagEx (22 бита), procEx-фильтр (NormalHit/CriticalHit/Dodge/Parry/Block/…).
/// T3 ✅ — ICD (spell_proc_event.cooldown) per-player per-aura.
/// T4 ✅ — PPM-механика для weapon-based проков (chance = speed_ms * ppm / 600).
/// T5 ✅ — spellFamilyName/spellFamilyMask фильтр триггеранта.
/// Не покрыто: T6 (смок-тесты целевых спеллов в живом клиенте) + специальные типы aura-handler'ов
/// CMaNGOS (HandleDummyAuraProc, HandleProcTriggerSpellAuraProc, HandleHasteAuraProc и т.п. — пока
/// унифицированный шанс-триггер для всех).
/// </remarks>
internal sealed class ProcService(SpellCatalog spellCatalog, AuraService auras)
{
    /// <summary>
    /// Событие <paramref name="procFlag"/> произошло на сессии <paramref name="session"/>.
    /// Для каждой активной прок-ауры с этим флагом проверяем условия (procEx/family/school/ICD), катим шанс
    /// (procChance / PPM / customChance) и накладываем триггер-спелл.
    /// </summary>
    /// <param name="session">Сторона, на чьих аурах смотрим. <c>Deal*</c>-флаги — атакующий, <c>Take*</c> — жертва.</param>
    /// <param name="procFlag">Какое событие произошло (полная маска допустима).</param>
    /// <param name="procEx">Исход события (CriticalHit/Dodge/Parry/Block/…) для T2-фильтра. None = обычный хит.</param>
    /// <param name="spellSchoolMask">Маска школ триггеранта (0 — авто-удар, физика).</param>
    /// <param name="sourceSpellId">spell_id триггерующего спелла (0 — авто-удар), для T5 family-фильтра.</param>
    /// <param name="weaponAttackSpeedMs">Скорость оружия (для PPM T4). 0 — не weapon-based прок.</param>
    internal async Task TryProcAsync(
        WorldSession session,
        ProcFlag procFlag,
        CancellationToken ct,
        ProcFlagEx procEx = ProcFlagEx.NormalHit,
        byte spellSchoolMask = 0,
        uint sourceSpellId = 0,
        uint weaponAttackSpeedMs = 0)
    {
        if (session.Progression.Auras.Count == 0)
            return;

        var now = Environment.TickCount64;
        SpellCatalog.SpellInfo? sourceSpell = null; // ленивая загрузка для T5

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

            var ev = await spellCatalog.GetProcEventAsync(aura.SpellId, ct);

            // procFlags: spell_proc_event override (если задан), иначе из Spell.dbc.
            var effectiveFlags = (ProcFlag)(ev is { ProcFlags: not 0 } ? ev.ProcFlags : info.ProcFlags);
            if ((effectiveFlags & procFlag) == 0)
                continue;

            // T2: procEx-фильтр. Если в event'е заданы биты — событие должно дать хотя бы один из них.
            // Если procEx == 0 — старое поведение (бьют только Hit/Crit, не фильтруем).
            if (ev is { ProcEx: not 0 } && (ev.ProcEx & (uint)procEx) == 0)
                continue;

            // SchoolMask: ограничение по школе триггера (если задано).
            if (ev is { SchoolMask: not 0 } && (ev.SchoolMask & spellSchoolMask) == 0)
                continue;

            // T5: family-фильтр триггеранта. Если ev.SpellFamilyName != 0, источник прока должен
            // принадлежать тому же семейству и хотя бы одна family-маска должна совпасть.
            if (ev is { SpellFamilyName: not 0 })
            {
                if (sourceSpellId == 0)
                    continue; // авто-удар не подходит к family-фильтру

                sourceSpell ??= await spellCatalog.GetAsync(sourceSpellId, ct);
                if (sourceSpell is null || sourceSpell.FamilyName != ev.SpellFamilyName)
                    continue;

                var evMaskLow = (ulong)ev.SpellFamilyMaskA0 | ((ulong)ev.SpellFamilyMaskA1 << 32);
                var evMaskHigh = ev.SpellFamilyMaskA2;
                if (evMaskLow == 0 && evMaskHigh == 0)
                {
                    // пустая маска при заданном family — допускаем (только по имени семейства)
                }
                else if ((sourceSpell.FamilyFlags & evMaskLow) == 0
                    && (sourceSpell.FamilyFlags2 & evMaskHigh) == 0)
                {
                    continue;
                }
            }

            // T3: ICD (skin cooldown). Если ev.Cooldown задан — блокируем повтор до истечения.
            if (ev is { Cooldown: > 0 })
            {
                if (session.Progression.ProcLastFiredMs.TryGetValue(aura.SpellId, out var last)
                    && now - last < ev.Cooldown)
                    continue;
            }

            // T4: расчёт шанса. Приоритет: PPM (weapon-based) > CustomChance > Spell.dbc procChance.
            float chancePct;
            if (ev is { PpmRate: > 0 } && weaponAttackSpeedMs > 0)
            {
                // chance% = weapon_speed_s * ppm / 60 * 100 = weapon_speed_ms * ppm / 600.
                chancePct = weaponAttackSpeedMs * ev.PpmRate / 600.0f;
            }
            else if (ev is { CustomChance: > 0 })
            {
                chancePct = ev.CustomChance;
            }
            else
            {
                chancePct = info.ProcChance; // 0 — трактуем как «всегда» (см. ниже)
            }

            if (chancePct > 0 && Random.Shared.NextDouble() * 100.0 >= chancePct)
                continue;

            var trig = await spellCatalog.GetAsync(info.ProcTriggerSpellId, ct);
            await auras.ApplyAsync(session, info.ProcTriggerSpellId, trig?.AuraDurationMs ?? 0,
                positive: true, form: 0, ct);

            // T3: фиксируем момент прока для ICD.
            if (ev is { Cooldown: > 0 })
                session.Progression.ProcLastFiredMs[aura.SpellId] = now;

            session.Logger.LogDebug(
                "PROC '{User}': {Aura} ({Flag} {Ex}, шанс {Chance:F1}%, source={Src}) → триггер {Trigger}",
                session.Account, aura.SpellId, procFlag, procEx, chancePct, sourceSpellId, info.ProcTriggerSpellId);
        }
    }
}
