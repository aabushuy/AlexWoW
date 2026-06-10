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

        // M11.2/M11.5: спелл изучает профессию (Effect SKILL/SKILL_STEP) → выдать навык-линию. Потолок
        // (тир: апрентис 75 … грандмастер 450) берём из BasePoints эффекта; не понижаем уже имеющийся
        // навык/потолок (изучение след. тира поднимает кап).
        try
        {
            var tpl = await session.WorldDb.GetSpellAsync(spellId, ct);
            if (tpl is not null && World.Professions.SkillGrantedBy(tpl) is { } grant)
            {
                var existing = session.SkillBook.Get(grant.SkillId);
                int curValue = existing?.Value ?? 0;
                int curMax = existing?.Max ?? 0;
                var value = (ushort)Math.Max(curValue, 1);
                var max = (ushort)Math.Max(curMax, (int)grant.Max);
                await Skills.GrantAsync(session, grant.SkillId, value, max, ct);

                // Доп. спеллы профессии (напр. Mining → Smelting: окно плавки). 2656 — эффект TRADE_SKILL,
                // навык не выдаёт → без рекурсии.
                if (World.Professions.AutoGrantSpells.TryGetValue(grant.SkillId, out var extras))
                    foreach (var extra in extras)
                        await GrantAsync(session, extra, ct);
            }
        }
        catch { /* БД мира недоступна — навык не выдаём, спелл всё равно изучен */ }

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
}
