using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Изучение спелла (M9.3 → M10.3, DI-сервис M7 S6 — бывший статик SpellLearn): добавляет абилку в
/// кэш/персист и шлёт клиенту либо <c>SMSG_LEARNED_SPELL</c> (новая абилка), либо
/// <c>SMSG_SUPERCEDED_SPELL</c> (высший ранг заменяет известный низший — кнопка/книга апгрейдятся, низший
/// убирается из книги). Единая точка для тренера, талантов, <c>.learn</c> и <c>.learnall</c>. Цепочки
/// рангов — <c>spell_chain.prev_spell</c> (mangos). Сверено с CMaNGOS <c>Player::SendSupercededSpell</c>
/// (SMSG_SUPERCEDED_SPELL = u32 old + u32 new).
/// </summary>
internal sealed class SpellLearnService(
    SkillsService skills,
    SpellModifierService spellMods,
    IWorldRepository worldDb,
    ICharacterStateRepository charState)
{
    /// <summary>
    /// Выдаёт спелл игроку. Возвращает true, если абилка действительно изучена (была неизвестна).
    /// При наличии известного предыдущего ранга шлёт SUPERCEDED(prev → spell) вместо LEARNED.
    /// Низший ранг из <see cref="Net.SessionState.SessionProgressionState.KnownSpells"/> НЕ удаляем (остаётся кастуемым).
    /// </summary>
    internal async Task<bool> GrantAsync(WorldSession session, uint spellId, CancellationToken ct)
    {
        if (session.InWorldGuid == 0 || !session.Progression.KnownSpells.Add(spellId))
            return false; // вне мира или уже известен

        await charState.AddLearnedSpellAsync(session.InWorldGuid, spellId, ct);

        SpellTemplateData? tpl = null;
        try { tpl = await worldDb.GetSpellAsync(spellId, ct); }
        catch { /* БД мира недоступна — просто LEARNED */ }

        // M11.2/M11.5/#2: профессия — учитель (LEARN_SPELL) учит спелл-открывашку; либо спелл сам выдаёт
        // навык/тир. Если обработали — клиенту уже сообщили (LEARNED открывашки / SUPERCEDED тира).
        if (tpl is not null && await TryGrantProfessionAsync(session, tpl, ct))
            return true;

        uint prev = 0;
        try { prev = await worldDb.GetPrevRankAsync(spellId, ct); }
        catch { /* БД мира недоступна — просто LEARNED */ }

        // M10.6: пассивный талант-модификатор (ауры 107/108) → в реестр сессии (моды prev-ранга снимаются).
        if (tpl is not null)
            spellMods.OnSpellLearned(session, tpl, prev);

        if (prev != 0 && session.Progression.KnownSpells.Contains(prev))
        {
            await session.SendAsync(WorldOpcode.SmsgSupercededSpell,
                new ByteWriter(8).UInt32(prev).UInt32(spellId).ToArray(), ct);
        }
        else
        {
            await session.SendAsync(WorldOpcode.SmsgLearnedSpell,
                new ByteWriter(6).UInt32(spellId).UInt16(0).ToArray(), ct);
        }

        return true;
    }

    /// <summary>
    /// Профессиональная обработка изучаемого спелла:
    /// <list type="bullet">
    /// <item>спелл-учитель (LEARN_SPELL) — учит реальный спелл-открывашку окна (он попадёт в книгу),
    /// сам учитель в книге не показывается;</item>
    /// <item>спелл-открывашка/тир — выдаёт навык-линию (потолок-тир из BasePoints), supercede'ит прежний
    /// тир в книге, выдаёт доп. спеллы (Mining → Smelting).</item>
    /// </list>
    /// Возвращает true, если уже сообщил клиенту (LEARNED открывашки / SUPERCEDED) — обычный LEARNED не нужен.
    /// </summary>
    private async Task<bool> TryGrantProfessionAsync(WorldSession session, SpellTemplateData tpl, CancellationToken ct)
    {
        // Учитель (LEARN_SPELL): выдаём навык/тир из его SKILL_STEP, учим реальный спелл (открывашку),
        // доп. спеллы; сам учитель в книгу не идёт (return true, LEARNED не шлём).
        var taught = World.Professions.TaughtSpell(tpl);
        if (taught != 0)
        {
            await GrantSkillFromAsync(session, tpl, ct);
            await GrantAsync(session, taught, ct);       // изучаемый спелл-открывашка → попадает в книгу
            await GrantAutoSpellsAsync(session, tpl, ct);
            return true;
        }

        if (World.Professions.SkillGrantedBy(tpl) is not { } grant)
            return false;

        await GrantSkillFromAsync(session, tpl, ct);

        var superceded = false;
        // #3: апгрейд тира — прежний тир-спелл заменяется в книге (SUPERCEDED + убрать из персиста).
        if (session.Progression.ProfessionRankSpell.TryGetValue(grant.SkillId, out var prevTier)
            && prevTier.Spell != tpl.Id && grant.Max >= prevTier.Max)
        {
            await session.SendAsync(WorldOpcode.SmsgSupercededSpell,
                new ByteWriter(8).UInt32(prevTier.Spell).UInt32(tpl.Id).ToArray(), ct);
            session.Progression.KnownSpells.Remove(prevTier.Spell);
            await charState.RemoveLearnedSpellAsync(session.InWorldGuid, prevTier.Spell, ct);
            superceded = true;
        }
        if (!session.Progression.ProfessionRankSpell.TryGetValue(grant.SkillId, out var curTier) || grant.Max >= curTier.Max)
            session.Progression.ProfessionRankSpell[grant.SkillId] = (tpl.Id, grant.Max);

        await GrantAutoSpellsAsync(session, tpl, ct);
        return superceded;
    }

    /// <summary>Выдаёт навык-линию по SKILL/SKILL_STEP спелла (не понижая текущее значение/потолок).</summary>
    private async Task GrantSkillFromAsync(WorldSession session, SpellTemplateData tpl, CancellationToken ct)
    {
        if (World.Professions.SkillGrantedBy(tpl) is not { } grant)
            return;
        var existing = session.Progression.SkillBook.Get(grant.SkillId);
        int curValue = existing?.Value ?? 0;
        int curMax = existing?.Max ?? 0;
        await skills.GrantAsync(session, grant.SkillId,
            (ushort)Math.Max(curValue, 1), (ushort)Math.Max(curMax, (int)grant.Max), ct);
    }

    /// <summary>Доп. спеллы профессии (Mining → Smelting). Выдаваемые навык не несут → без рекурсии.</summary>
    private async Task GrantAutoSpellsAsync(WorldSession session, SpellTemplateData tpl, CancellationToken ct)
    {
        if (World.Professions.SkillGrantedBy(tpl) is { } g
            && World.Professions.AutoGrantSpells.TryGetValue(g.SkillId, out var extras))
        {
            foreach (var extra in extras)
                await GrantAsync(session, extra, ct);
        }
    }
}
