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
