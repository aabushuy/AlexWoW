using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Лут с трупа (M6.6): при смерти существа роллим лут (деньги + предметы из creature_loot_template),
/// помечаем труп lootable (UNIT_DYNAMIC_FLAGS). Игрок открывает окно (CMSG_LOOT), забирает деньги
/// (CMSG_LOOT_MONEY) и предметы (CMSG_AUTOSTORE_LOOT_ITEM), закрывает (CMSG_LOOT_RELEASE).
/// Тап/раздел добычи в группе — позже; сейчас обыскать может любой наблюдатель.
/// </summary>
public static class LootHandlers
{
    /// <summary>UNIT_DYNFLAG_LOOTABLE — труп подсвечивается и кликается для обыска.</summary>
    private const uint DynFlagLootable = 0x1;

    private const byte LootTypeCorpse = 1;        // SMSG_LOOT_RESPONSE loot_method = CORPSE
    private const byte LootSlotAllow = 0;         // LootSlotType TYPE_ALLOW_LOOT

    /// <summary>
    /// Существо убито: роллим лут и помечаем труп lootable (для наблюдателей). Зовётся из путей
    /// смерти существа (мили M6.3 / спелл M6.4). При недоступности БД/пустом луте — труп не lootable.
    /// </summary>
    internal static async Task OnCreatureKilledAsync(WorldSession session, WorldCreature creature, CancellationToken ct)
    {
        // M6.5: зачёт убийства в цели активных квестов.
        await QuestHandlers.CreditCreatureAsync(session, creature.Template.Entry, creature.Guid, ct);

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
            if (d.Chance < 0 && !QuestHandlers.NeedsQuestItem(session, d.ItemId))
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

    [WorldOpcodeHandler(WorldOpcode.CmsgLoot)]
    public static async Task OnLoot(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var guid = packet.Reader().UInt64();
        var creature = session.World.FindCreature(guid);
        if (creature is null || !creature.Lootable || creature.Loot is not { } loot)
            return; // нечего лутать

        session.LootGuid = guid;
        await session.SendAsync(WorldOpcode.SmsgLootResponse, BuildLootResponse(guid, loot), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgLootMoney)]
    public static async Task OnLootMoney(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var creature = session.World.FindCreature(session.LootGuid);
        if (creature?.Loot is not { } loot || loot.Gold == 0)
            return;

        var gold = loot.Gold;
        loot.Gold = 0;
        session.Money += gold;
        await session.Characters.SetMoneyAsync(session.InWorldGuid, session.Money, ct);

        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildCoinageUpdate(session.InWorldGuid, session.Money), ct);
        await session.SendAsync(WorldOpcode.SmsgLootClearMoney, [], ct);
        await ClearLootIfEmptyAsync(session, creature, ct);
        session.Logger.LogInformation("LOOT money '{User}': +{Gold} меди (всего {Money})",
            session.Account, gold, session.Money);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAutostoreLootItem)]
    public static async Task OnAutostoreLootItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var slotIndex = packet.Reader().UInt8();
        var creature = session.World.FindCreature(session.LootGuid);
        if (creature?.Loot is not { } loot)
            return;
        var slot = loot.Slots.FirstOrDefault(s => s.Index == slotIndex && !s.Taken);
        if (slot is null)
            return;

        var item = await InventoryGrant.TryGiveAsync(session, slot.ItemId, slot.Count, ct);
        if (item is null)
            return; // рюкзак полон — оставляем предмет в трупе (клиент покажет «инвентарь полон»)

        slot.Taken = true;
        await session.SendAsync(WorldOpcode.SmsgLootRemoved, new ByteWriter(1).UInt8(slotIndex).ToArray(), ct);
        await ClearLootIfEmptyAsync(session, creature, ct);
        session.Logger.LogInformation("LOOT item '{User}': {Item} x{Count}", session.Account, slot.ItemId, slot.Count);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgLootRelease)]
    public static async Task OnLootRelease(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var guid = packet.Reader().UInt64();
        session.LootGuid = 0;
        await session.SendAsync(WorldOpcode.SmsgLootReleaseResponse,
            new ByteWriter(9).UInt64(guid).UInt8(1).ToArray(), ct);
    }

    /// <summary>Весь лут разобран → снимаем lootable-флаг (труп перестаёт подсвечиваться).</summary>
    private static async Task ClearLootIfEmptyAsync(WorldSession session, WorldCreature creature, CancellationToken ct)
    {
        if (creature.Loot is not { IsEmpty: true })
            return;
        creature.Lootable = false;
        creature.Loot = null;
        await session.World.BroadcastToObserversAsync(creature, WorldOpcode.SmsgUpdateObject,
            CreatureUpdate.BuildDynamicFlagsUpdate(creature.Guid, 0), ct);
    }

    /// <summary>
    /// SMSG_LOOT_RESPONSE (3.3.5): u64 guid + u8 loot_type + u32 gold + u8 item_count +
    /// [u8 slot + u32 item + u32 count + u32 displayid + u32 randomSuffix + u32 randomProperty + u8 slot_type].
    /// Layout по CMaNGOS (wow_messages упрощает item — для 3.3.5 клиента нужен полный набор полей).
    /// </summary>
    private static byte[] BuildLootResponse(ulong guid, CreatureLoot loot)
    {
        var items = loot.Slots.Where(s => !s.Taken).ToList();
        var w = new ByteWriter(32 + items.Count * 22);
        w.UInt64(guid);
        w.UInt8(LootTypeCorpse);
        w.UInt32(loot.Gold);
        w.UInt8((byte)items.Count);
        foreach (var s in items)
            w.UInt8(s.Index)
             .UInt32(s.ItemId)
             .UInt32(s.Count)
             .UInt32(s.DisplayId)
             .UInt32(0)            // random suffix
             .UInt32(0)            // random property id
             .UInt8(LootSlotAllow);
        return w.ToArray();
    }
}
