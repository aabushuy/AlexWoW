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
    }

    /// <summary>
    /// Инкрементальное обновление при изучении спелла (тренер/талант/.learn): модификаторы предыдущего
    /// ранга снимаются (низший остаётся в книге, но эффект даёт только высший), новые добавляются.
    /// Без I/O — шаблон и prev уже получены в <see cref="SpellLearnService.GrantAsync"/>.
    /// </summary>
    internal void OnSpellLearned(WorldSession session, SpellTemplateData tpl, uint prevRank)
    {
        if (prevRank != 0)
            session.Progression.SpellMods.RemoveAll(m => m.SourceSpell == prevRank);
        if (SpellModifiers.ExtractFrom(tpl) is { } extracted)
        {
            session.Progression.SpellMods.AddRange(extracted);
            session.Logger.LogDebug("SPELLMODS '{User}': +{Count} от спелла {Spell}",
                session.Account, extracted.Count, tpl.Id);
        }
    }

    /// <summary>Снимает модификаторы источника при удалении спелла (сброс талантов). M9.8/M10.6.</summary>
    internal void OnSpellRemoved(WorldSession session, uint spellId)
        => session.Progression.SpellMods.RemoveAll(m => m.SourceSpell == spellId);
}
