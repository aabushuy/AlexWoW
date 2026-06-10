using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Крафт профессии (M11.3): спелл-рецепт с эффектом CREATE_ITEM расходует реагенты и создаёт предмет,
/// затем — ролл прокачки навыка. Реагенты/результат — из <c>spell_template</c> (см. <see cref="SpellCatalog"/>),
/// привязка рецепт→навык для skill-up — <see cref="Professions.Recipes"/>.
/// </summary>
public static class Crafting
{
    /// <summary>Есть ли у игрока все реагенты рецепта (для отказа на старте каста).</summary>
    public static bool HasReagents(WorldSession session, SpellCatalog.SpellInfo info)
    {
        if (info.Reagents is null)
            return true;
        foreach (var (item, count) in info.Reagents)
            if (InventoryGrant.CountItem(session, item) < count)
                return false;
        return true;
    }

    /// <summary>Завершение крафта: повторная проверка реагентов → расход → создание результата → skill-up.</summary>
    public static async Task DoCraftAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        CancellationToken ct)
    {
        if (info.CreateItemId == 0 || !HasReagents(session, info))
            return;

        if (info.Reagents is not null)
            foreach (var (item, count) in info.Reagents)
                await InventoryGrant.ConsumeAsync(session, item, count, ct);

        var placed = await InventoryGrant.TryGiveAsync(session, info.CreateItemId, info.CreateItemCount, ct);
        session.Logger.LogInformation("CRAFT '{User}': spell={Spell} → {Count}×{Item}{Full}",
            session.Account, spellId, info.CreateItemCount, info.CreateItemId,
            placed is null ? " (нет места — результат потерян)" : "");

        await SkillUpForRecipeAsync(session, spellId, ct);
    }

    /// <summary>Кэш привязки рецепт→(навык, req) из npc_trainer (иммутабельно), чтобы не дёргать БД каждый крафт.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, (ushort SkillId, ushort ReqSkill)?> RecipeSkillCache = new();

    /// <summary>
    /// Ролл прокачки навыка за крафт. Привязка рецепт→навык: сначала сид <see cref="Professions.Recipes"/>
    /// (плавка и пр., которых нет у тренера), затем npc_trainer (reqskill/reqskillvalue) — покрывает все
    /// рецепты, изучаемые у тренера. Формула цвета — <see cref="Professions.SkillUpChance"/>. M11.5.
    /// </summary>
    private static async Task SkillUpForRecipeAsync(WorldSession session, uint spellId, CancellationToken ct)
    {
        (ushort SkillId, ushort ReqSkill)? recipe = Professions.Recipes.TryGetValue(spellId, out var seed)
            ? (seed.SkillId, seed.ReqSkill)
            : await ResolveFromTrainerAsync(session, spellId, ct);
        if (recipe is not { } r)
            return;

        var sk = session.SkillBook.Get(r.SkillId);
        if (sk is null)
            return; // профессия не изучена — не качаем
        var chance = Professions.SkillUpChance(sk.Value, r.ReqSkill);
        if (chance > 0 && Random.Shared.Next(100) < chance)
            await Skills.AddValueAsync(session, r.SkillId, 1, ct);
    }

    private static async Task<(ushort SkillId, ushort ReqSkill)?> ResolveFromTrainerAsync(
        WorldSession session, uint spellId, CancellationToken ct)
    {
        if (RecipeSkillCache.TryGetValue(spellId, out var cached))
            return cached;
        (ushort, ushort)? result = null;
        try { result = await session.WorldDb.GetRecipeSkillAsync(spellId, ct); }
        catch { /* БД мира недоступна — без skill-up */ }
        RecipeSkillCache[spellId] = result;
        return result;
    }
}
