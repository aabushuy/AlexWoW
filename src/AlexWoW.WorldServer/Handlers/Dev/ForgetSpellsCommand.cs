using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.forgetspells</c> — забыть изученные навыки/абилки персонажа (из <c>character_spell</c> и сессии),
/// антипод <c>.learnall</c>. Сохраняет стартовые классовые/языковые спеллы (их нет в <c>character_spell</c>),
/// а также <b>таланты и профессии</b>: их ранг-спеллы тоже лежат в <c>character_spell</c>, но снимаются
/// отдельно (<see cref="ResetTalentsCommand"/> / <see cref="ProfCommand"/>) — снос их здесь рассинхронил бы
/// панель талантов/окно профессии. Поэтому ранг-спеллы талантов и спеллы профессий исключаются.
/// </summary>
internal sealed class ForgetSpellsCommand(
    ICharacterStateRepository charState,
    IWorldRepository worldDb,
    SpellModifierService spellMods) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["forgetspells"];
    public string Help => ".forgetspells";
    public int Order => 51; // рядом с .learnall (50)
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var session = ctx.Session;
        var keep = await BuildKeepSetAsync(session, ct);

        var removed = 0;
        foreach (var sp in await charState.GetLearnedSpellsAsync(session.InWorldGuid, ct))
        {
            if (keep.Contains(sp))
                continue;
            session.Progression.KnownSpells.Remove(sp);
            await spellMods.OnSpellRemovedAsync(session, sp, ct); // снять моды, если это был пассив
            await charState.RemoveLearnedSpellAsync(session.InWorldGuid, sp, ct);
            await session.SendAsync(WorldOpcode.SmsgRemovedSpell, new ByteWriter(4).UInt32(sp).ToArray(), ct);
            removed++;
        }

        await ctx.ReplyAsync($"Забыто навыков: {removed} (таланты и профессии не затронуты)", ct);
    }

    /// <summary>Спеллы, которые НЕ трогаем: ранг-спеллы изученных талантов + спеллы профессий.</summary>
    private async Task<HashSet<uint>> BuildKeepSetAsync(WorldSession session, CancellationToken ct)
    {
        var keep = new HashSet<uint>();

        // Профессии: апрентис-открывашки + авто-спеллы (плавка и т.п.) + текущие тиры.
        foreach (var sp in World.Professions.ApprenticeSpellByKeyword.Values)
            keep.Add(sp);
        foreach (var arr in World.Professions.AutoGrantSpells.Values)
            foreach (var sp in arr)
                keep.Add(sp);
        foreach (var tier in session.Progression.ProfessionRankSpell.Values)
            keep.Add(tier.Spell);

        // Таланты: все ранг-спеллы изученных талантов (снимаются через .resettalents).
        if (session.Progression.LearnedTalents.Count > 0)
        {
            try
            {
                var all = await worldDb.GetAllTalentsAsync(ct);
                foreach (var (tid, rank) in session.Progression.LearnedTalents)
                    if (all.TryGetValue(tid, out var t))
                        for (var r = 0; r <= rank; r++)
                        {
                            var sp = t.RankSpell(r);
                            if (sp != 0)
                                keep.Add(sp);
                        }
            }
            catch { /* БД мира недоступна — таланты не исключим (лучше, чем падение команды) */ }
        }

        return keep;
    }
}
