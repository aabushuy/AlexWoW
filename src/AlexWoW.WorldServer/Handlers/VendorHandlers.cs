using AlexWoW.Common.Network;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Торговля с NPC (M6.2): открытие окна (gossip-hello/list inventory из npc_vendor),
/// покупка (CMSG_BUY_ITEM) и продажа (CMSG_SELL_ITEM). Деньги — PLAYER_FIELD_COINAGE.
/// </summary>
public static class VendorHandlers
{
    /// <summary>entry шаблона существа из его GUID (0xF130 | entry&lt;&lt;24 | counter).</summary>
    private static uint CreatureEntry(ulong guid) => (uint)((guid >> 24) & 0xFFFFFF);

    [WorldOpcodeHandler(WorldOpcode.CmsgGossipHello, WorldOpcode.CmsgListInventory)]
    public static async Task OnListInventory(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var vendorGuid = reader.UInt64();
        var entry = CreatureEntry(vendorGuid);

        IReadOnlyList<VendorItem> items;
        try { items = await session.WorldDb.GetVendorItemsAsync(entry, ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug("LIST_INVENTORY {Entry}: БД мира недоступна ({Msg})", entry, ex.Message);
            return;
        }

        if (items.Count == 0)
            return; // не вендор (или пустой) — окно не открываем

        await session.SendAsync(WorldOpcode.SmsgListInventory, VendorList.Build(vendorGuid, items), ct);
        session.Logger.LogDebug("LIST_INVENTORY вендор entry={Entry}: {Count} товаров", entry, items.Count);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgBuyItem)]
    public static async Task OnBuyItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var vendorGuid = reader.UInt64();
        var itemEntry = reader.UInt32();
        reader.UInt32();                 // slot (muid) — не используем, ищем по entry
        var amount = reader.UInt8();
        if (amount == 0) amount = 1;

        async Task FailAsync(BuyResult r) => await session.SendAsync(WorldOpcode.SmsgBuyFailed,
            new ByteWriter(13).UInt64(vendorGuid).UInt32(itemEntry).UInt8((byte)r).ToArray(), ct);

        var entry = CreatureEntry(vendorGuid);
        IReadOnlyList<VendorItem> items;
        try { items = await session.WorldDb.GetVendorItemsAsync(entry, ct); }
        catch { await FailAsync(BuyResult.CantFindItem); return; }

        var vi = items.FirstOrDefault(x => x.ItemId == itemEntry);
        if (vi is null) { await FailAsync(BuyResult.CantFindItem); return; }

        // amount — число «лотов»; цена за лот = BuyPrice, предметов в лоте = BuyCount.
        var buyCount = vi.BuyCount == 0 ? 1u : vi.BuyCount;
        var qty = amount * buyCount;          // сколько физических предметов выдать
        var cost = vi.BuyPrice * amount;
        if (session.Money < cost) { await FailAsync(BuyResult.NotEnoughMoney); return; }

        var slot = FreeBackpackSlot(session);
        if (slot < 0) { await FailAsync(BuyResult.InventoryFull); return; }

        var ownerGuid = session.InWorldGuid;
        var itemLow = await session.Characters.AddItemAsync(ownerGuid, itemEntry,
            InventorySlots.MainBag, (byte)slot, qty, ct);
        var item = new InventoryItem
        {
            ItemGuid = itemLow, OwnerGuid = ownerGuid, ItemEntry = itemEntry,
            Bag = InventorySlots.MainBag, Slot = (byte)slot, StackCount = qty,
        };
        session.Inventory.Add(item);

        session.Money -= cost;
        await session.Characters.SetMoneyAsync(ownerGuid, session.Money, ct);

        // Создать предмет у клиента, привязать к слоту рюкзака, обновить деньги, подтвердить покупку.
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            ItemObject.BuildItemsCreate(new[] { item }, ownerGuid), ct);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildInvSlotUpdate(ownerGuid, slot, ItemObject.ItemGuid(itemLow)), ct);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildCoinageUpdate(ownerGuid, session.Money), ct);

        var buy = new ByteWriter(20)
            .UInt64(vendorGuid)
            .UInt32(vi.Slot + 1)
            .UInt32(vi.MaxCount == 0 ? 0xFFFFFFFFu : vi.MaxCount)
            .UInt32(qty);
        await session.SendAsync(WorldOpcode.SmsgBuyItem, buy.ToArray(), ct);
        session.Logger.LogInformation("BUY '{User}': item={Item} x{Qty} (лотов {Amt}) за {Cost}, осталось {Money}",
            session.Account, itemEntry, qty, amount, cost, session.Money);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgSellItem)]
    public static async Task OnSellItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var vendorGuid = reader.UInt64();
        var itemGuid = reader.UInt64();
        // u8 amount — продаём всю стопку (стартовые предметы не стопкуются).

        async Task FailAsync(SellResult r) => await session.SendAsync(WorldOpcode.SmsgSellItem,
            new ByteWriter(17).UInt64(vendorGuid).UInt64(itemGuid).UInt8((byte)r).ToArray(), ct);

        var itemLow = (uint)(itemGuid & 0xFFFFFFFF);
        var item = session.Inventory.FirstOrDefault(i => i.ItemGuid == itemLow);
        if (item is null) { await FailAsync(SellResult.CantFindItem); return; }

        ItemTemplateData? t;
        try { t = await session.WorldDb.GetItemTemplateAsync(item.ItemEntry, ct); }
        catch { await FailAsync(SellResult.CantFindItem); return; }
        if (t is null || t.SellPrice == 0) { await FailAsync(SellResult.CantSellItem); return; }

        var ownerGuid = session.InWorldGuid;
        var gain = t.SellPrice * Math.Max(1u, item.StackCount);

        await session.Characters.RemoveItemAsync(itemLow, ct);
        session.Inventory.Remove(item);
        session.Money += gain;
        await session.Characters.SetMoneyAsync(ownerGuid, session.Money, ct);

        // Убрать предмет у клиента, очистить слот, обновить деньги.
        await session.SendAsync(WorldOpcode.SmsgDestroyObject,
            new ByteWriter(9).UInt64(ItemObject.ItemGuid(itemLow)).UInt8(0).ToArray(), ct);
        if (item.Bag == InventorySlots.MainBag)
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                PlayerSpawn.BuildInvSlotUpdate(ownerGuid, item.Slot, 0), ct);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildCoinageUpdate(ownerGuid, session.Money), ct);
        session.Logger.LogInformation("SELL '{User}': item={Item} (guid={Guid}) за {Gain}, теперь {Money}",
            session.Account, item.ItemEntry, itemLow, gain, session.Money);
    }

    /// <summary>Первый свободный слот рюкзака (23..38) основного контейнера; -1 если нет места.</summary>
    private static int FreeBackpackSlot(WorldSession session)
    {
        var taken = new HashSet<byte>();
        foreach (var i in session.Inventory)
            if (i.Bag == InventorySlots.MainBag)
                taken.Add(i.Slot);
        for (var s = InventorySlots.BackpackStart; s < InventorySlots.BackpackEnd; s++)
            if (!taken.Contains((byte)s))
                return s;
        return -1;
    }
}

/// <summary>Коды SMSG_BUY_FAILED (BuyResult, 3.3.5a).</summary>
internal enum BuyResult : byte
{
    CantFindItem = 0,
    ItemAlreadySold = 1,
    NotEnoughMoney = 2,
    SellerDontLikeYou = 4,
    DistanceTooFar = 5,
    ItemSoldOut = 7,
    InventoryFull = 8,
}

/// <summary>Коды SMSG_SELL_ITEM (SellItemResult, 3.3.5a).</summary>
internal enum SellResult : byte
{
    CantFindItem = 1,
    CantSellItem = 2,
    CantFindVendor = 3,
    YouDontOwnThatItem = 4,
}
