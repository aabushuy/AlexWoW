using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Изучение спелла (M9.3 → M10.3): добавляет абилку в кэш/персист и шлёт клиенту либо
/// <c>SMSG_LEARNED_SPELL</c> (новая абилка), либо <c>SMSG_SUPERCEDED_SPELL</c> (высший ранг заменяет
/// известный низший — кнопка/книга апгрейдятся, низший убирается из книги). Единая точка для тренера,
/// <c>.learn</c> и <c>.learnall</c>. Цепочки рангов — <c>spell_chain.prev_spell</c> (mangos).
/// Сверено с CMaNGOS <c>Player::SendSupercededSpell</c> (SMSG_SUPERCEDED_SPELL = u32 old + u32 new).
/// </summary>
public static class SpellLearn
{
    /// <summary>
    /// Выдаёт спелл игроку. Возвращает true, если абилка действительно изучена (была неизвестна).
    /// При наличии известного предыдущего ранга шлёт SUPERCEDED(prev → spell) вместо LEARNED.
    /// Низший ранг из <see cref="WorldSession.KnownSpells"/> НЕ удаляем (остаётся кастуемым).
    /// </summary>
    internal static async Task<bool> GrantAsync(WorldSession session, uint spellId, CancellationToken ct)
    {
        if (session.InWorldGuid == 0 || !session.KnownSpells.Add(spellId))
            return false; // вне мира или уже известен

        await session.CharState.AddLearnedSpellAsync(session.InWorldGuid, spellId, ct);

        // M11.2/M11.5: спелл изучает профессию (Effect SKILL/SKILL_STEP) → выдать навык-линию + обработать
        // тиры. Если это апгрейд тира — шлём SUPERCEDED(старый→новый) вместо LEARNED (в книге один спелл).
        if (await TryGrantProfessionAsync(session, spellId, ct))
            return true;

        uint prev = 0;
        try { prev = await session.WorldDb.GetPrevRankAsync(spellId, ct); }
        catch { /* БД мира недоступна — просто LEARNED */ }

        if (prev != 0 && session.KnownSpells.Contains(prev))
            await session.SendAsync(WorldOpcode.SmsgSupercededSpell,
                new ByteWriter(8).UInt32(prev).UInt32(spellId).ToArray(), ct);
        else
            await session.SendAsync(WorldOpcode.SmsgLearnedSpell,
                new ByteWriter(6).UInt32(spellId).UInt16(0).ToArray(), ct);
        return true;
    }

    /// <summary>
    /// Если спелл изучает профессию: выдаёт навык-линию (потолок-тир из BasePoints, не понижая текущий),
    /// supercede'ит прежний тир-спелл этой профессии в книге и выдаёт доп. спеллы (Mining → Smelting).
    /// Возвращает true, если уже сообщил клиенту через SUPERCEDED (LEARNED слать не нужно).
    /// </summary>
    private static async Task<bool> TryGrantProfessionAsync(WorldSession session, uint spellId, CancellationToken ct)
    {
        try
        {
            var tpl = await session.WorldDb.GetSpellAsync(spellId, ct);
            if (tpl is null || World.Professions.SkillGrantedBy(tpl) is not { } grant)
                return false;

            var existing = session.SkillBook.Get(grant.SkillId);
            int curValue = existing?.Value ?? 0;
            int curMax = existing?.Max ?? 0;
            var value = (ushort)Math.Max(curValue, 1);
            var max = (ushort)Math.Max(curMax, (int)grant.Max);
            await Skills.GrantAsync(session, grant.SkillId, value, max, ct);

            var superceded = false;
            // #3: апгрейд тира — прежний тир-спелл заменяется в книге (SUPERCEDED + убрать из персиста).
            if (session.ProfessionRankSpell.TryGetValue(grant.SkillId, out var prevTier)
                && prevTier.Spell != spellId && grant.Max >= prevTier.Max)
            {
                await session.SendAsync(WorldOpcode.SmsgSupercededSpell,
                    new ByteWriter(8).UInt32(prevTier.Spell).UInt32(spellId).ToArray(), ct);
                session.KnownSpells.Remove(prevTier.Spell);
                await session.CharState.RemoveLearnedSpellAsync(session.InWorldGuid, prevTier.Spell, ct);
                superceded = true;
            }
            // Запоминаем текущий тир профессии (для следующего апгрейда/дедупа).
            if (!session.ProfessionRankSpell.TryGetValue(grant.SkillId, out var curTier) || grant.Max >= curTier.Max)
                session.ProfessionRankSpell[grant.SkillId] = (spellId, grant.Max);

            // Доп. спеллы профессии (Mining → Smelting: окно плавки). 2656 навык не выдаёт → без рекурсии.
            if (World.Professions.AutoGrantSpells.TryGetValue(grant.SkillId, out var extras))
                foreach (var extra in extras)
                    await GrantAsync(session, extra, ct);

            return superceded;
        }
        catch
        {
            return false; // БД мира недоступна — навык не выдаём, спелл всё равно изучен (LEARNED)
        }
    }
}
