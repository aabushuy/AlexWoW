using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Таланты (M9.6+): отправка состояния (<c>SMSG_TALENTS_INFO</c>), изучение и сброс. В каркасе (M9.6) —
/// только отправка очков/изученных при входе; деревья клиент рисует сам из своей DBC. Изучение
/// (<c>CMSG_LEARN_TALENT</c>) и сброс — M9.7/M9.8.
/// </summary>
public static class TalentHandlers
{
    /// <summary>Шлёт текущее состояние талантов игрока (свободные очки + изученные таланты). M9.6.</summary>
    public static Task SendTalentsInfoAsync(WorldSession session, CancellationToken ct)
    {
        var talents = session.LearnedTalents.Select(kv => (kv.Key, kv.Value)).ToList();
        return session.SendAsync(WorldOpcode.SmsgTalentsInfo,
            TalentPackets.BuildTalentsInfo(session.TalentPoints, talents), ct);
    }

    /// <summary>Пересчитывает свободные очки талантов: MaxPoints(класс,уровень) − потрачено (Σ rank+1). M9.6.</summary>
    public static void RecomputePoints(WorldSession session, byte classId, byte level)
    {
        var max = World.TalentMath.MaxPoints(classId, level);
        uint spent = 0;
        foreach (var rank in session.LearnedTalents.Values)
            spent += (uint)(rank + 1);
        session.TalentPoints = max > spent ? max - spent : 0u;
    }

    /// <summary>
    /// CMSG_LEARN_TALENT (M9.7): вложить очко в талант. Валидация (CMaNGOS <c>Player::LearnTalent</c>):
    /// класс↔дерево, следующий ранг, свободное очко, тир-гейт (5×Tier очков в этом дереве), пререквизит.
    /// Грант ранг-спелла через <see cref="SpellLearn.GrantAsync"/> (книга/персист), запись таланта, пересчёт очков.
    /// </summary>
    [WorldOpcodeHandler(WorldOpcode.CmsgLearnTalent)]
    public static async Task OnLearnTalent(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var talentId = r.UInt32();
        var requestedRank = r.UInt32();
        if (session.InWorldGuid == 0 || session.Character is not { } c)
            return;

        IReadOnlyDictionary<uint, TalentData> all;
        try { all = await session.WorldDb.GetAllTalentsAsync(ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug("LEARN_TALENT: БД талантов недоступна ({Msg})", ex.Message);
            return;
        }
        if (!all.TryGetValue(talentId, out var t))
            return;

        // Класс ↔ дерево (classMask = 1<<(class-1)).
        var classMask = 1u << (c.Class - 1);
        if ((t.ClassMask & classMask) == 0)
            return;

        // Только СЛЕДУЮЩИЙ ранг за клик (0 если ещё не изучен).
        var learned = session.LearnedTalents.TryGetValue(talentId, out var curRank);
        var nextRank = learned ? curRank + 1 : 0;
        if (requestedRank != (uint)nextRank || nextRank >= t.MaxRank || t.RankSpell(nextRank) == 0)
            return;
        if (session.TalentPoints < 1)
            return;

        // Тир-гейт: суммарно потрачено в ЭТОМ дереве ≥ 5*Tier.
        if (t.Tier > 0)
        {
            uint spentInTab = 0;
            foreach (var (lid, lrank) in session.LearnedTalents)
                if (all.TryGetValue(lid, out var lt) && lt.TalentTab == t.TalentTab)
                    spentInTab += (uint)(lrank + 1);
            if (spentInTab < 5 * t.Tier)
                return;
        }

        // Пререквизит-талант.
        if (t.DependsOn != 0
            && (!session.LearnedTalents.TryGetValue(t.DependsOn, out var depRank) || depRank < t.DependsOnRank))
            return;

        // Выучить ранг-спелл (персист character_spell + LEARNED/SUPERCEDED), записать талант.
        var rankSpell = t.RankSpell(nextRank);
        await SpellLearn.GrantAsync(session, rankSpell, ct);
        session.LearnedTalents[talentId] = (byte)nextRank;
        await session.CharState.SetTalentRankAsync(session.InWorldGuid, talentId, (byte)nextRank, ct);

        // Пересчёт свободных очков + апдейт поля и панели.
        RecomputePoints(session, c.Class, c.Level);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid,
                m => m.SetUInt32(UpdateField.PlayerCharacterPoints1, session.TalentPoints)), ct);
        await SendTalentsInfoAsync(session, ct);
        session.Logger.LogInformation("LEARN_TALENT '{User}': talent={Tid} rank={Rank} spell={Spell}, очков {Points}",
            session.Account, talentId, nextRank, rankSpell, session.TalentPoints);
    }
}
