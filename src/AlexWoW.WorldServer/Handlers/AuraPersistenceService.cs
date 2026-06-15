using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Персист аур через релог (M7 #21/M10.5, выделено из Auras в M7 S3): восстановление сохранённых аур при
/// входе и сохранение временны́х при выходе. Зависит и от <see cref="AuraService"/> (иконки/формы), и от
/// <see cref="PeriodicsService"/> (HoT/+HP с остатком) — сами они друг на друга не замыкаются (разрыв цикла).
/// </summary>
internal sealed class AuraPersistenceService(
    AuraService auras,
    PeriodicsService periodics,
    SpellCatalog spellCatalog,
    ICharacterStateRepository charState)
{
    /// <summary>
    /// Восстанавливает сохранённые ауры при входе: перманентные переключатели (M7 #21, form-based, remaining=0)
    /// и временны́е баффы/HoT с остатком длительности (M10.5, remaining&gt;0 → <see cref="PeriodicsService"/>).
    /// Зовётся после спавна в OnPlayerLogin (MaxHealth уже пересчитан — +HP-баффы накладываются поверх).
    /// </summary>
    internal async Task ReapplyPersistedAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return;
        IReadOnlyList<(uint Spell, byte Form, uint RemainingMs)> saved;
        try { saved = await charState.GetAurasAsync(session.InWorldGuid, ct); }
        catch { return; }

        var level = (byte)(session.Character?.Level ?? 1);
        foreach (var (spell, form, remaining) in saved)
        {
            // Временны́е баффы/HoT (M10.5) — через систему периодики/аур с остатком длительности.
            if (remaining > 0)
            {
                await periodics.RestoreTimedAuraAsync(session, spell, (int)remaining, ct);
                continue;
            }

            // Перманентный переключатель (M7 #21). Восстанавливаем через AuraService с ГРУППОЙ
            // эксклюзивности из каталога тоглов — иначе при Group=0 смена стойки/ауры/аспекта не снимает
            // восстановленную, и они копятся («все активны одновременно»). Заодно это само-лечит дубли в
            // character_aura: восстановление второй стойки той же группы снимает первую (и её строку БД).
            if (SpellCatalog.TryGetToggle(spell, out var toggle))
            {
                // Стат-эффект формы (Shadowform +15% Shadow) — восстановить вместе с формой, иначе пропадёт до перетогла.
                var info = await spellCatalog.GetAsync(spell, ct);
                await auras.ApplyAsync(session, spell, durationMs: 0, positive: true, toggle.Form, ct,
                    group: toggle.Group, persist: true,
                    damageDonePct: info?.DamageDonePct ?? 0, damageDoneSchool: info?.DamageDoneSchoolMask ?? 0,
                    damageTakenPct: info?.DamageTakenPct ?? 0); // ресурс формы — централизованно в AuraService.ApplyAsync
                continue;
            }

            // Фолбэк: перманентная аура вне каталога тоглов (нештатно) — восстановить как есть, без группы.
            byte flags = (byte)(AuraFlags.Effect1 | AuraFlags.SelfCast | AuraFlags.Positive);
            var aura = new ActiveAura
            {
                SpellId = spell,
                Slot = AuraService.FirstFreeSlot(session),
                Flags = flags,
                ShapeshiftForm = form,
                Persist = true,
                DurationMs = 0,
                ExpiresAtMs = 0,
            };
            session.Progression.Auras.Add(aura);
            await session.World.BroadcastToPlayerObserversAsync(session.Player!, WorldOpcode.SmsgAuraUpdate,
                AuraPackets.BuildApply((ulong)session.InWorldGuid, aura.Slot, spell, flags, level, 1, 0), ct);
            if (form != 0)
            {
                session.Progression.ShapeshiftForm = form;
                await auras.BroadcastFormAsync(session, ct);
            }
        }
    }

    /// <summary>
    /// Сохраняет временны́е свои ауры (баффы/HoT) при выходе с остатком длительности (M10.5): собирает из
    /// <see cref="Net.SessionState.SessionProgressionState.Auras"/> те, что с таймером (DurationMs&gt;0, ещё не истёкшие), и перезаписывает
    /// их в БД. Переключатели (перманентные) персистятся отдельно при наложении. Зовётся из LeaveWorldAsync.
    /// </summary>
    internal async Task SaveTimedAurasAsync(WorldSession session, uint ownerGuid, CancellationToken ct)
    {
        if (ownerGuid == 0)
            return;
        var now = Environment.TickCount64;
        var timed = session.Progression.Auras
            .Where(a => a.DurationMs > 0 && a.ExpiresAtMs > now)
            .Select(a => (a.SpellId, (uint)(a.ExpiresAtMs - now)))
            .ToList();
        try { await charState.SaveTimedAurasAsync(ownerGuid, timed, ct); }
        catch { /* персист не критичен для текущей сессии */ }
    }
}
