using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Авто-харнесс проверки заклинаний (M12 Spell QA, SQA-4): прогоняет все известные боевые абилки класса по
/// манекенам N раз и пишет результаты через <see cref="SpellTestCaptureService"/>. Касты идут прямым вызовом
/// <see cref="SpellCastCompletion.CompleteCastAsync"/> (минуя каст-бар и гейты), ресурс рефиллится между кастами.
/// Урон/хил записываются штатным путём (хук в <see cref="SpellEffectsService"/>); DoT/HoT — синтетическим тиком.
/// </summary>
internal sealed class SpellTestHarnessService(
    SpellCatalog spellCatalog,
    SpellCastCompletion completion,
    SpellTestCaptureService capture,
    ILogger<SpellTestHarnessService> logger)
{
    private const int MaxCastsPerSpell = 50;

    /// <summary>Запускает прогон: возвращает число протестированных спеллов, либо −1 если нет персонажа в мире.</summary>
    internal async Task<int> RunAsync(WorldSession session, int castsPerSpell, CancellationToken ct)
    {
        if (session.Character is null || session.InWorldGuid == 0)
            return -1;

        // Оба манекена перед игроком (урон + лечебный).
        await session.World.SummonTrainingDummyAsync(session, ct);
        await session.World.SummonHealDummyAsync(session, ct);

        // Старт сессии захвата в режиме харнесса (если ещё не активна — тогда мы её и закроем в конце).
        var startedHere = await capture.StartAsync(session, SpellTestMode.Harness, "auto-harness", ct);

        var n = Math.Clamp(castsPerSpell, 1, MaxCastsPerSpell);
        var tested = 0;
        var skipped = 0;

        // Снимок ресурсов/позиции — восстановим после прогона (касты тратят ресурс и могли бы двигать игрока).
        var savedMana = session.Cast.Mana;
        var savedRage = session.Combat.Rage;
        var savedEnergy = session.Combat.Energy;
        float px = session.PosX, py = session.PosY, pz = session.PosZ, po = session.PosO;

        foreach (var spellId in session.Progression.KnownSpells.ToList())
        {
            if (SpellCatalog.TryGetToggle(spellId, out _))
                continue; // стойки/ауры/аспекты — не боевой каст

            SpellCatalog.SpellInfo? info;
            try { info = await spellCatalog.GetAsync(spellId, ct); }
            catch { info = null; }
            if (info is null)
                continue;

            // Двигающие спеллы (рывок/телепорт, в т.ч. через триггер) и крафт — пропускаем.
            if (info.Movement != SpellCatalog.SpellMovement.None || info.TriggerSpellId != 0 || info.CreateItemId != 0)
                continue;

            // Оставляем только спеллы с числовым эффектом: прямой урон/хил, weapon-абилки, DoT/HoT.
            var hasDirect = info.IsHeal || info.MaxAmount > 0 || info.WeaponDamage || info.WeaponPercent > 0;
            if (!hasDirect && !info.Periodic)
            {
                skipped++;
                continue;
            }

            // Цель: прямой хил → лечебный манекен; чистый HoT → сам игрок; всё остальное (урон/DoT) → урон-манекен.
            var target = info.IsHeal ? Npcs.HealDummyGuid
                : info is { PeriodicHeal: true } && !hasDirect ? (ulong)session.InWorldGuid
                : Npcs.TrainingDummyGuid;

            for (var i = 0; i < n; i++)
            {
                capture.SetCastIndex(session, i);
                // Рефилл ресурса, чтобы каждый каст гарантированно прошёл и игрок не остался без маны.
                session.Cast.Mana = session.Cast.MaxMana;
                session.Combat.Rage = 1000;
                session.Combat.Energy = 100;

                await completion.CompleteCastAsync(session, spellId, info, target, castCount: 0, ct);

                // Восстановить позицию (страховка от триггер-движения, хотя такие отфильтрованы).
                session.PosX = px; session.PosY = py; session.PosZ = pz; session.PosO = po;

                // DoT/HoT: эталонный тик пишем синтетически (без ожидания world-цикла; естественные тики
                // в режиме харнесса рекордер пропускает, чтобы не дублировать).
                if (info.Periodic)
                    await capture.RecordSyntheticTickAsync(session, spellId, info, ct);
            }
            tested++;
        }

        // Восстановить ресурсы/позицию игрока и снять накопившиеся кулдауны (чтобы тестировщик мог кастовать сам).
        session.Cast.Mana = savedMana;
        session.Combat.Rage = savedRage;
        session.Combat.Energy = savedEnergy;
        session.PosX = px; session.PosY = py; session.PosZ = pz; session.PosO = po;
        session.Cast.SpellCooldowns.Clear();

        if (startedHere)
            await capture.StopAsync(session, ct);

        logger.LogInformation("SpellTest harness '{User}': протестировано {Tested}, пропущено {Skipped}, ×{N}",
            session.Account, tested, skipped, n);
        return tested;
    }
}
