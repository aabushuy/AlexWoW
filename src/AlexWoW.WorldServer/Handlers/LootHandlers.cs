using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Лут с трупа (M6.6, DI-модуль M7 S5 — бывший статик): игрок открывает окно (CMSG_LOOT), забирает деньги
/// (CMSG_LOOT_MONEY) и предметы (CMSG_AUTOSTORE_LOOT_ITEM), закрывает (CMSG_LOOT_RELEASE). Ролл лута при
/// смерти существа и lootable-пометка — <see cref="KillRewardService"/>.
/// Тап/раздел добычи в группе — позже; сейчас обыскать может любой наблюдатель.
/// </summary>
internal sealed class LootHandlers(
    InventoryGrantService inventoryGrant,
    ICharacterRepository characters) : IOpcodeHandlerModule
{
    private const byte LootTypeCorpse = 1;        // SMSG_LOOT_RESPONSE loot_method = CORPSE
    private const byte LootSlotAllow = 0;         // LootSlotType TYPE_ALLOW_LOOT

    [WorldOpcodeHandler(WorldOpcode.CmsgLoot)]
    public async Task OnLoot(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var guid = packet.Reader().UInt64();
        var creature = session.World.FindCreature(guid);
        if (creature is null || !creature.Lootable || creature.Loot is not { } loot)
            return; // нечего лутать

        session.Inv.LootGuid = guid;
        await session.SendAsync(WorldOpcode.SmsgLootResponse, BuildLootResponse(guid, loot), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgLootMoney)]
    public async Task OnLootMoney(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var creature = session.World.FindCreature(session.Inv.LootGuid);
        if (creature?.Loot is not { } loot || loot.Gold == 0)
            return;

        var gold = loot.Gold;
        loot.Gold = 0;
        session.Inv.Money += gold;
        await characters.SetMoneyAsync(session.InWorldGuid, session.Inv.Money, ct);

        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildCoinageUpdate(session.InWorldGuid, session.Inv.Money), ct);
        await session.SendAsync(WorldOpcode.SmsgLootClearMoney, [], ct);
        await ClearLootIfEmptyAsync(session, creature, ct);
        session.Logger.LogInformation("LOOT money '{User}': +{Gold} меди (всего {Money})",
            session.Account, gold, session.Inv.Money);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAutostoreLootItem)]
    public async Task OnAutostoreLootItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var slotIndex = packet.Reader().UInt8();
        var creature = session.World.FindCreature(session.Inv.LootGuid);
        if (creature?.Loot is not { } loot)
            return;
        var slot = loot.Slots.FirstOrDefault(s => s.Index == slotIndex && !s.Taken);
        if (slot is null)
            return;

        var item = await inventoryGrant.TryGiveAsync(session, slot.ItemId, slot.Count, ct);
        if (item is null)
            return; // рюкзак полон — оставляем предмет в трупе (клиент покажет «инвентарь полон»)

        slot.Taken = true;
        await session.SendAsync(WorldOpcode.SmsgLootRemoved, new ByteWriter(1).UInt8(slotIndex).ToArray(), ct);
        await ClearLootIfEmptyAsync(session, creature, ct);
        session.Logger.LogInformation("LOOT item '{User}': {Item} x{Count}", session.Account, slot.ItemId, slot.Count);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgLootRelease)]
    public async Task OnLootRelease(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var guid = packet.Reader().UInt64();
        session.Inv.LootGuid = 0;
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
