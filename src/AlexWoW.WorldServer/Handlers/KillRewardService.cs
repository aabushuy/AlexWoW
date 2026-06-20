using AlexWoW.Database.Abstractions;
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
internal sealed class KillRewardService(
    QuestProgressService questProgress,
    ProgressionService progression,
    ComboPointService comboPoints,
    InventoryGrantService inventoryGrant,
    ProcService procs,
    GroupRegistry groupRegistry,
    AuraStateService auraState,
    IWorldRepository worldDb)
{
    /// <summary>UNIT_DYNFLAG_LOOTABLE — труп подсвечивается и кликается для обыска.</summary>
    private const uint DynFlagLootable = 0x1;

    /// <summary>
    /// GROUP.T4: групповой XP-rate (CMaNGOS XP::xp_in_group_rate). Сумма rate близка к 1 — XP делится
    /// между членами + small-group bonus 3-5 человек.
    /// </summary>
    internal static float GroupXpRate(int memberCount) => memberCount switch
    {
        <= 1 => 1.0f,
        2 => 1.0f,
        3 => 1.166f,
        4 => 1.3f,
        5 => 1.4f,
        _ => 0.5f,    // рейд — половина (CMaNGOS xp_in_group_rate raid branch)
    };

    /// <summary>
    /// Существо убито: роллим лут и помечаем труп lootable (для наблюдателей). При недоступности БД/пустом
    /// луте — труп не lootable.
    /// </summary>
    internal async Task OnCreatureKilledAsync(WorldSession session, WorldCreature creature, CancellationToken ct)
    {
        // SPELL.T3 Warrior Victory Rush: окно 20с после убийства — взводим тайм-штамп и AURA_STATE (бит 6 в
        // UNIT_FIELD_AURASTATE) только для класса воина. Клиент подсветит кнопку Victory Rush; cast-резолвер
        // проверит окно через AuraStateService.HasState(state=7).
        var now = Environment.TickCount64;
        session.Combat.LastKillMs = now;
        if (session.Character?.Class == 1)
            await auraState.SetVictoryRushAsync(session, now, ct);

        // CP.2: очки серии теряются со смертью комбо-цели (no-op, если копились на другой/уже расходованы).
        await comboPoints.ClearForTargetAsync(session, creature.Guid, ct);

        // PROC.T1 (порт CMaNGOS UnitAuraProcHandler): Kill — на ауры убийцы при убийстве существа.
        // Покрывает Bloodthirst-like эффекты и пассивки на kill (Vampiric Touch и т.п. — когда ауры подключим).
        await procs.TryProcAsync(session, ProcFlag.Kill, ct);

        // §2 Drain Soul (ЧК): убито существо, помеченное Drain Soul → игрок получает осколок души (item 6265).
        if (session.Combat.DrainSoulTargetGuid == creature.Guid)
        {
            session.Combat.DrainSoulTargetGuid = 0;
            await inventoryGrant.TryGiveAsync(session, SpellCatalog.SoulShardItem, 1, ct);
            session.Logger.LogInformation("SOULSHARD '{User}': осколок души за Drain Soul-убийство '{Name}'",
                session.Account, creature.Template.Name);
        }

        // M6.5: зачёт убийства в цели активных квестов.
        await questProgress.CreditCreatureAsync(session, creature.Template.Entry, creature.Guid, ct);

        // M9.1 + GROUP.T4: опыт за убийство. Если убийца в группе — XP делится между онлайн-членами
        // (упрощённо — все онлайн считаются «в радиусе»; уточнение по дистанции в T5/raid).
        // CMaNGOS Group::RewardGroupAtKill + XP::xp_in_group_rate: 1=1.0, 2=1.0, 3=1.166, 4=1.3, 5=1.4.
        if (session.Character is { } pc && pc.Level < World.LevelStore.MaxLevel)
        {
            var baseXp = session.World.Levels.KillXp(pc.Level, creature.Template.Level);
            if (baseXp > 0)
            {
                var killerGuid = (ulong)session.InWorldGuid;
                var group = groupRegistry.GetByChar(killerGuid);
                if (group is not null && group.IsCreated)
                {
                    var onlineMembers = group.Members.Where(m => m.IsOnline).ToList();
                    var count = onlineMembers.Count;
                    var rate = GroupXpRate(count);
                    var perMember = (uint)(baseXp * rate / count);
                    foreach (var m in onlineMembers)
                    {
                        var target = session.World.FindPlayer(m.Guid);
                        if (target is null || target.Character.Level >= World.LevelStore.MaxLevel)
                            continue;
                        if (perMember > 0)
                            await progression.GiveXpAsync(target.Session, perMember, ct);
                    }
                }
                else
                {
                    await progression.GiveXpAsync(session, baseXp, ct);
                }
            }
        }

        Database.Models.CreatureLootData? data;
        try
        {
            data = await worldDb.GetCreatureLootAsync(creature.Template.Entry, ct);
        }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "Лут {Entry}: БД мира недоступна ({Msg})", creature.Template.Entry, ex.Message);
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
