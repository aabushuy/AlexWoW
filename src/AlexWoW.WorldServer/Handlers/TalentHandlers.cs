using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Таланты (M9.6+, DI-модуль M7 S6): отправка состояния (<c>SMSG_TALENTS_INFO</c>), изучение
/// (<c>CMSG_LEARN_TALENT</c>, M9.7) и сброс (M9.8). Деревья клиент рисует сам из своей DBC.
/// </summary>
internal sealed class TalentHandlers(
    SpellLearnService spellLearn,
    SpellModifierService spellMods,
    IWorldRepository worldDb,
    ICharacterRepository characters,
    ICharacterStateRepository charState) : IOpcodeHandlerModule
{
    /// <summary>Шлёт текущее состояние талантов игрока (свободные очки + изученные таланты). M9.6.</summary>
    internal Task SendTalentsInfoAsync(WorldSession session, CancellationToken ct)
    {
        var talents = session.Progression.LearnedTalents.Select(kv => (kv.Key, kv.Value)).ToList();
        return session.SendAsync(WorldOpcode.SmsgTalentsInfo,
            TalentPackets.BuildTalentsInfo(session.Progression.TalentPoints, talents), ct);
    }

    /// <summary>Пересчитывает свободные очки талантов: MaxPoints(класс,уровень) − потрачено (Σ rank+1). M9.6.</summary>
    internal void RecomputePoints(WorldSession session, byte classId, byte level)
    {
        var max = World.TalentMath.MaxPoints(classId, level);
        uint spent = 0;
        foreach (var rank in session.Progression.LearnedTalents.Values)
            spent += (uint)(rank + 1);
        session.Progression.TalentPoints = max > spent ? max - spent : 0u;
    }

    /// <summary>
    /// CMSG_LEARN_TALENT (M9.7): вложить очко в талант. Валидация (CMaNGOS <c>Player::LearnTalent</c>):
    /// класс↔дерево, следующий ранг, свободное очко, тир-гейт (5×Tier очков в этом дереве), пререквизит.
    /// Грант ранг-спелла через <see cref="SpellLearnService.GrantAsync"/> (книга/персист), запись таланта, пересчёт очков.
    /// </summary>
    [WorldOpcodeHandler(WorldOpcode.CmsgLearnTalent)]
    public async Task OnLearnTalent(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var talentId = r.UInt32();
        var requestedRank = r.UInt32();
        if (session.InWorldGuid == 0 || session.Character is not { } c)
            return;

        IReadOnlyDictionary<uint, TalentData> all;
        try { all = await worldDb.GetAllTalentsAsync(ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "LEARN_TALENT: БД талантов недоступна ({Msg})", ex.Message);
            return;
        }
        if (!all.TryGetValue(talentId, out var t))
            return;

        // Класс ↔ дерево (classMask = 1<<(class-1)).
        var classMask = 1u << (c.Class - 1);
        if ((t.ClassMask & classMask) == 0)
            return;

        // Только СЛЕДУЮЩИЙ ранг за клик (0 если ещё не изучен).
        var learned = session.Progression.LearnedTalents.TryGetValue(talentId, out var curRank);
        var nextRank = learned ? curRank + 1 : 0;
        if (requestedRank != (uint)nextRank || nextRank >= t.MaxRank || t.RankSpell(nextRank) == 0)
            return;
        if (session.Progression.TalentPoints < 1)
            return;

        // Тир-гейт: суммарно потрачено в ЭТОМ дереве ≥ 5*Tier.
        if (t.Tier > 0)
        {
            uint spentInTab = 0;
            foreach (var (lid, lrank) in session.Progression.LearnedTalents)
            {
                if (all.TryGetValue(lid, out var lt) && lt.TalentTab == t.TalentTab)
                    spentInTab += (uint)(lrank + 1);
            }

            if (spentInTab < 5 * t.Tier)
                return;
        }

        // Пререквизит-талант.
        if (t.DependsOn != 0
            && (!session.Progression.LearnedTalents.TryGetValue(t.DependsOn, out var depRank) || depRank < t.DependsOnRank))
        {
            return;
        }

        // Выучить ранг-спелл (персист character_spell + LEARNED/SUPERCEDED), записать талант.
        var rankSpell = t.RankSpell(nextRank);
        await spellLearn.GrantAsync(session, rankSpell, ct);
        // M10.6: ранг-спеллы талантов НЕ записаны в spell_chain → GrantAsync не снял модификаторы
        // прежнего ранга; снимаем по цепочке из talent (иначе ранги пассивок суммируются).
        if (nextRank > 0)
            spellMods.OnSpellRemoved(session, t.RankSpell(nextRank - 1));
        session.Progression.LearnedTalents[talentId] = (byte)nextRank;
        await charState.SetTalentRankAsync(session.InWorldGuid, talentId, (byte)nextRank, ct);

        // Пересчёт свободных очков + апдейт поля и панели.
        RecomputePoints(session, c.Class, c.Level);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid,
                m => m.SetUInt32(UpdateField.PlayerCharacterPoints1, session.Progression.TalentPoints)), ct);
        await SendTalentsInfoAsync(session, ct);
        session.Logger.LogInformation("LEARN_TALENT '{User}': talent={Tid} rank={Rank} spell={Spell}, очков {Points}",
            session.Account, talentId, nextRank, rankSpell, session.Progression.TalentPoints);
    }

    private const uint Gold = 10000;

    /// <summary>Стоимость следующего сброса по последней (CMaNGOS resetTalentsCost): 1/5/10g, далее +5g, кап 50g.</summary>
    private static uint NextResetCost(uint stored) =>
        stored < Gold ? Gold :
        stored < 5 * Gold ? 5 * Gold :
        stored < 10 * Gold ? 10 * Gold :
        Math.Min(stored + 5 * Gold, 50 * Gold);

    /// <summary>Госсип «Сбросить таланты» → MSG_TALENT_WIPE_CONFIRM (npc + стоимость): открывает окно
    /// подтверждения у клиента. M9.8.</summary>
    internal Task SendWipeConfirmAsync(WorldSession session, ulong npcGuid, CancellationToken ct)
    {
        var cost = NextResetCost(session.Character?.TalentResetCost ?? 0);
        return session.SendAsync(WorldOpcode.MsgTalentWipeConfirm,
            new ByteWriter(12).UInt64(npcGuid).UInt32(cost).ToArray(), ct);
    }

    /// <summary>MSG_TALENT_WIPE_CONFIRM (клиент подтвердил, u64 npc): сброс талантов за золото. M9.8.</summary>
    [WorldOpcodeHandler(WorldOpcode.MsgTalentWipeConfirm)]
    public async Task OnTalentWipeConfirm(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        packet.Reader().UInt64(); // npc guid — не используем
        if (session.InWorldGuid == 0 || session.Character is not { } c)
            return;
        var cost = NextResetCost(c.TalentResetCost);
        if (session.Progression.LearnedTalents.Count == 0 || session.Inv.Money < cost)
            return; // нечего сбрасывать / нет денег
        await ResetTalentsAsync(session, cost, ct);
    }

    /// <summary>
    /// Сброс всех талантов: снять ранг-спеллы изученных талантов (все ранги 0..current) из книги/персиста
    /// (<c>SMSG_REMOVED_SPELL</c>), очистить character_talent, вернуть очки, списать <paramref name="cost"/>
    /// (0 — бесплатно, для дев-команды D5). M9.8.
    /// </summary>
    internal async Task ResetTalentsAsync(WorldSession session, uint cost, CancellationToken ct)
    {
        var c = session.Character!;
        IReadOnlyDictionary<uint, TalentData> all;
        try { all = await worldDb.GetAllTalentsAsync(ct); }
        catch { all = new Dictionary<uint, TalentData>(); }

        foreach (var (talentId, rank) in session.Progression.LearnedTalents)
        {
            if (!all.TryGetValue(talentId, out var t))
                continue;
            for (var rr = 0; rr <= rank; rr++)
            {
                var sp = t.RankSpell(rr);
                if (sp == 0 || !session.Progression.KnownSpells.Remove(sp))
                    continue;
                spellMods.OnSpellRemoved(session, sp); // M10.6: снять модификаторы пассивного таланта
                await charState.RemoveLearnedSpellAsync(session.InWorldGuid, sp, ct);
                await session.SendAsync(WorldOpcode.SmsgRemovedSpell, new ByteWriter(4).UInt32(sp).ToArray(), ct);
            }
        }

        session.Progression.LearnedTalents.Clear();
        await charState.ClearTalentsAsync(session.InWorldGuid, ct);

        if (cost > 0)
        {
            session.Inv.Money -= cost;
            await characters.SetMoneyAsync(session.InWorldGuid, session.Inv.Money, ct);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                PlayerSpawn.BuildCoinageUpdate((ulong)session.InWorldGuid, session.Inv.Money), ct);
            c.TalentResetCost = cost;
            await characters.SetTalentResetCostAsync(session.InWorldGuid, cost, ct);
        }

        RecomputePoints(session, c.Class, c.Level);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid,
                m => m.SetUInt32(UpdateField.PlayerCharacterPoints1, session.Progression.TalentPoints)), ct);
        await SendTalentsInfoAsync(session, ct);
        session.Logger.LogInformation("TALENT WIPE '{User}': сброшено, очков {Points}, списано {Cost}",
            session.Account, session.Progression.TalentPoints, cost);
    }
}
