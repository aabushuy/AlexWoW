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

    /// <summary>Ролл прокачки навыка за крафт (если рецепт привязан к навыку в сиде). M11.5-формула.</summary>
    private static async Task SkillUpForRecipeAsync(WorldSession session, uint spellId, CancellationToken ct)
    {
        if (!Professions.Recipes.TryGetValue(spellId, out var recipe))
            return;
        var sk = session.SkillBook.Get(recipe.SkillId);
        if (sk is null)
            return; // профессия не изучена — не качаем
        var chance = Professions.SkillUpChance(sk.Value, recipe.ReqSkill);
        if (chance > 0 && Random.Shared.Next(100) < chance)
            await Skills.AddValueAsync(session, recipe.SkillId, 1, ct);
    }
}
