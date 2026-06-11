using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Реестр модификаторов спеллов на сессии (M10.6): пассивные таланты с аурами ADD_FLAT/PCT_MODIFIER
/// (107/108) собираются в <see cref="Net.SessionState.SessionProgressionState.SpellMods"/> и применяются
/// в точках расчёта (стоимость — <see cref="SpellCastService.EffectivePowerCost"/>, урон —
/// <see cref="SpellEffectsService"/>, тики/длительность — <see cref="PeriodicsService"/>, каст-тайм/КД —
/// <see cref="SpellCastService.HandleCastAsync"/>). Математика — <see cref="SpellModifiers"/>.
/// Эталон — CMaNGOS <c>Aura::HandleAddModifier</c>/<c>Player::ApplySpellMod</c>.
/// </summary>
internal sealed class SpellModifierService(ISpellTemplateRepository spells, ITalentRepository talents)
{
    /// <summary>
    /// Перестраивает реестр по KnownSpells (вход в мир): батч-чтение шаблонов, извлечение аур 107/108,
    /// ранг-дедуп — низший ранг остаётся в KnownSpells, но эффект даёт только высший. Цепочки рангов:
    /// spell_chain (тренируемые) + ранги талантов из <c>talent</c> (ранг-спеллы талантов в spell_chain
    /// НЕ записаны — как CMaNGOS строит chain из TalentEntry). БД недоступна → без модификаторов.
    /// </summary>
    internal async Task RebuildAsync(WorldSession session, CancellationToken ct)
    {
        var mods = session.Progression.SpellMods;
        mods.Clear();
        if (session.Progression.KnownSpells.Count == 0)
            return;
        try
        {
            var templates = await spells.GetSpellsAsync([.. session.Progression.KnownSpells], ct);
            var candidates = new List<SpellModifier>();
            foreach (var tpl in templates)
            {
                if (SpellModifiers.ExtractFrom(tpl) is { } extracted)
                    candidates.AddRange(extracted);
            }

            if (candidates.Count == 0)
                return;

            var sourceIds = candidates.Select(m => m.SourceSpell).ToHashSet();
            var superseded = (await spells.GetPrevRanksAsync([.. sourceIds], ct)).Values.ToHashSet();
            foreach (var t in (await talents.GetAllTalentsAsync(ct)).Values)
            {
                // Из рангов одного таланта среди кандидатов активен только высший.
                var top = -1;
                for (var r = 0; r < 5; r++)
                {
                    if (t.RankSpell(r) != 0 && sourceIds.Contains(t.RankSpell(r)))
                        top = r;
                }
                for (var r = 0; r < top; r++)
                {
                    if (t.RankSpell(r) != 0)
                        superseded.Add(t.RankSpell(r));
                }
            }

            mods.AddRange(candidates.Where(m => !superseded.Contains(m.SourceSpell)));
            session.Logger.LogDebug("SPELLMODS '{User}': активно {Count} (кандидатов {Cand})",
                session.Account, mods.Count, candidates.Count);
        }
        catch (Exception ex)
        {
            session.Logger.LogError(ex, "SPELLMODS '{User}': ошибка сборки реестра — таланты без эффекта",
                session.Account);
        }

        await SyncClientAsync(session, ct);
    }

    /// <summary>
    /// Инкрементальное обновление при изучении спелла (тренер/талант/.learn): модификаторы предыдущего
    /// ранга снимаются (низший остаётся в книге, но эффект даёт только высший), новые добавляются.
    /// Без чтения БД — шаблон и prev уже получены в <see cref="SpellLearnService.GrantAsync"/>.
    /// </summary>
    internal async Task OnSpellLearnedAsync(WorldSession session, SpellTemplateData tpl, uint prevRank, CancellationToken ct)
    {
        var changed = prevRank != 0 && session.Progression.SpellMods.RemoveAll(m => m.SourceSpell == prevRank) > 0;
        if (SpellModifiers.ExtractFrom(tpl) is { } extracted)
        {
            session.Progression.SpellMods.AddRange(extracted);
            changed = true;
            session.Logger.LogDebug("SPELLMODS '{User}': +{Count} от спелла {Spell}",
                session.Account, extracted.Count, tpl.Id);
        }
        if (changed)
            await SyncClientAsync(session, ct);
    }

    /// <summary>Снимает модификаторы источника при удалении спелла (сброс талантов). M9.8/M10.6.</summary>
    internal async Task OnSpellRemovedAsync(WorldSession session, uint spellId, CancellationToken ct)
    {
        if (session.Progression.SpellMods.RemoveAll(m => m.SourceSpell == spellId) > 0)
            await SyncClientAsync(session, ct);
    }

    /// <summary>
    /// Доносит модификаторы до КЛИЕНТА (SMSG_SET_FLAT/PCT_SPELL_MODIFIER): без этого тултип/кнопка
    /// считают базовую стоимость из клиентской DBC (Удар героя «15 ярости» и не жмётся при 14, хотя
    /// сервер списал бы 14). Итог на каждый затронутый бит маски (0–95) по каждому op — как CMaNGOS
    /// <c>Player::AddSpellMod</c>; дифф с прошлой отправкой, исчезнувшие биты зануляются (сброс талантов).
    /// </summary>
    private static async Task SyncClientAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return;

        // Итоги: (бит, op, pct) → сумма значений всех модификаторов, чья маска накрывает бит.
        var totals = new Dictionary<(byte Eff, byte Op, bool Pct), int>();
        foreach (var mod in session.Progression.SpellMods)
        {
            for (byte eff = 0; eff < 96; eff++)
            {
                var word = eff < 32 ? mod.Mask1 : eff < 64 ? mod.Mask2 : mod.Mask3;
                if ((word & (1u << (eff % 32))) == 0)
                    continue;
                var key = (eff, (byte)mod.Op, mod.IsPct);
                totals[key] = totals.GetValueOrDefault(key) + mod.Value;
            }
        }

        var sent = session.Progression.SentSpellModTotals;
        // Исчезнувшие биты → 0 (клиент снимает модификатор).
        foreach (var key in sent.Keys.Where(k => !totals.ContainsKey(k)).ToList())
        {
            await SendModAsync(session, key.Eff, key.Op, key.Pct, 0, ct);
            sent.Remove(key);
        }
        foreach (var (key, value) in totals)
        {
            if (sent.TryGetValue(key, out var old) && old == value)
                continue;
            await SendModAsync(session, key.Eff, key.Op, key.Pct, value, ct);
            sent[key] = value;
        }
    }

    private static Task SendModAsync(WorldSession session, byte eff, byte op, bool pct, int value, CancellationToken ct)
        => session.SendAsync(
            pct ? Protocol.WorldOpcode.SmsgSetPctSpellModifier : Protocol.WorldOpcode.SmsgSetFlatSpellModifier,
            Protocol.SpellPackets.BuildSpellModifier(eff, op, value), ct);
}
