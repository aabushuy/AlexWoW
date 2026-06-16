using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.prof &lt;prof&gt; learn|forget</c> — выучить/забыть профессию напрямую у игрока (§177), без спавна
/// тренера. «Выучить» учит апрентис-спелл профессии через <see cref="SpellLearnService"/> (он выдаёт навык-
/// линию, окно профессии и доп. спеллы вроде плавки). «Забыть» снимает спелл-открывашку, доп. спеллы и
/// текущий тир-спелл из книги + убирает навык (<see cref="SkillsService.ForgetAsync"/>). Маппинг профессий —
/// <see cref="World.Professions"/>.
/// </summary>
internal sealed class ProfCommand(
    SpellLearnService spellLearn,
    SkillsService skills,
    ICharacterStateRepository charState) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["prof"];
    public string Help => ".prof <prof> learn|forget";
    public int Order => 101; // рядом с .proftrainer (100)
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var prof = ctx.ArgLower(0);
        var action = ctx.ArgLower(1);
        if (!World.Professions.ApprenticeSpellByKeyword.TryGetValue(prof, out var opener)
            || !World.Professions.SkillIdByKeyword.TryGetValue(prof, out var skillId))
        {
            await ctx.ReplyAsync("Профессия: tailoring/blacksmithing/leatherworking/alchemy/enchanting/engineering/jewelcrafting/mining/herbalism/skinning/cooking/firstaid/fishing; действие: learn|forget", ct);
            return;
        }

        var name = World.Professions.SkillName(skillId);
        switch (action)
        {
            case "learn":
                await spellLearn.GrantAsync(ctx.Session, opener, ct);
                await ctx.ReplyAsync($"Профессия изучена: {name}", ct);
                break;
            case "forget":
                var had = await ForgetAsync(ctx.Session, skillId, opener, ct);
                await ctx.ReplyAsync(had ? $"Профессия забыта: {name}" : $"Профессия не изучена: {name}", ct);
                break;
            default:
                await ctx.ReplyAsync("Действие: learn|forget", ct);
                break;
        }
    }

    /// <summary>Снимает спеллы профессии (открывашка + авто-спеллы + текущий тир) и навык-линию.</summary>
    private async Task<bool> ForgetAsync(WorldSession session, ushort skillId, uint opener, CancellationToken ct)
    {
        var spells = new List<uint> { opener };
        if (World.Professions.AutoGrantSpells.TryGetValue(skillId, out var extras))
            spells.AddRange(extras);
        // Текущий тир (подмастерье/эксперт/…) может отличаться от апрентиса — снять и его.
        if (session.Progression.ProfessionRankSpell.TryGetValue(skillId, out var tier) && tier.Spell != 0)
            spells.Add(tier.Spell);

        foreach (var sp in spells.Distinct())
        {
            if (!session.Progression.KnownSpells.Remove(sp))
                continue;
            await charState.RemoveLearnedSpellAsync(session.InWorldGuid, sp, ct);
            await session.SendAsync(WorldOpcode.SmsgRemovedSpell, new ByteWriter(4).UInt32(sp).ToArray(), ct);
        }
        session.Progression.ProfessionRankSpell.Remove(skillId);

        return await skills.ForgetAsync(session, skillId, ct);
    }
}
