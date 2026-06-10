using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Награда за убийство существа (M6.5/M6.6/M9.1, DI-сервис M7 S5 — вынос из LootHandlers): зачёт в цели
/// квестов, опыт убийце, ролл лута (деньги + предметы из creature_loot_template) и lootable-пометка трупа.
/// Зовётся из путей смерти существа (мили M6.3 / спелл M6.4 / DoT M10.4b). Окно/раздача лута —
/// модуль <see cref="LootHandlers"/>.
/// </summary>
internal sealed class KillRewardService(QuestProgressService questProgress, ProgressionService progression)
{
    /// <summary>UNIT_DYNFLAG_LOOTABLE — труп подсвечивается и кликается для обыска.</summary>
    private const uint DynFlagLootable = 0x1;

    /// <summary>
    /// Существо убито: роллим лут и помечаем труп lootable (для наблюдателей). При недоступности БД/пустом
    /// луте — труп не lootable.
    /// </summary>
    internal async Task OnCreatureKilledAsync(WorldSession session, WorldCreature creature, CancellationToken ct)
    {
        // M6.5: зачёт убийства в цели активных квестов.
        await questProgress.CreditCreatureAsync(session, creature.Template.Entry, creature.Guid, ct);

        // M9.1: опыт за убийство (убийце).
        if (session.Character is { } pc && pc.Level < World.LevelStore.MaxLevel)
        {
            var xp = session.World.Levels.KillXp(pc.Level, creature.Template.Level);
            if (xp > 0)
                await progression.GiveXpAsync(session, xp, ct);
        }

        Database.Models.CreatureLootData? data;
        try
        {
            data = await session.WorldDb.GetCreatureLootAsync(creature.Template.Entry, ct);
        }
        catch (Exception ex)
        {
            session.Logger.LogDebug("Лут {Entry}: БД мира недоступна ({Msg})", creature.Template.Entry, ex.Message);
            return;
        }
        if (data is null)
            return; // у существа нет лута

        var gold = data.MaxGold > 0
            ? (uint)Random.Shared.Next((int)data.MinGold, (int)data.MaxGold + 1)
            : 0u;

        var slots = new List<LootSlot>();
        byte index = 0;
        foreach (var d in data.Drops)
        {
            // M6.10: квест-предметы (Chance < 0) падают только держателю нужного квеста, по |chance|%.
            if (d.Chance < 0 && !questProgress.NeedsQuestItem(session, d.ItemId))
                continue;
            if (Random.Shared.NextDouble() * 100.0 >= Math.Abs(d.Chance))
                continue; // не выпал
            var lo = (uint)Math.Max(1, d.MinCount);
            var hi = Math.Max(lo, d.MaxCount);
            var count = lo == hi ? lo : (uint)Random.Shared.Next((int)lo, (int)hi + 1);
            slots.Add(new LootSlot { Index = index++, ItemId = d.ItemId, Count = count, DisplayId = d.DisplayId });
        }

        if (gold == 0 && slots.Count == 0)
            return; // ничего не выпало

        creature.Loot = new CreatureLoot { Gold = gold, Slots = slots };
        creature.Lootable = true;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgUpdateObject,
            CreatureUpdate.BuildDynamicFlagsUpdate(creature.Guid, DynFlagLootable), ct);
        session.Logger.LogDebug("Лут трупа '{Name}': {Gold} меди, {Items} предметов",
            creature.Template.Name, gold, slots.Count);
    }
}
