using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Торговля с NPC (M6.2, DI-модуль M7 S6): открытие окна (gossip-hello/list inventory из npc_vendor),
/// покупка (CMSG_BUY_ITEM) и продажа (CMSG_SELL_ITEM). Деньги — PLAYER_FIELD_COINAGE.
/// Класс невелик — листинг/покупка/продажа в одном модуле; госсип (<see cref="GossipService"/>)
/// инжектит модуль и зовёт <see cref="SendVendorListAsync"/>.
/// </summary>
internal sealed class VendorHandlers(
    InventoryGrantService inventoryGrant,
    IWorldRepository worldDb,
    ICharacterRepository characters,
    IInventoryRepository items) : IOpcodeHandlerModule
{
    /// <summary>entry шаблона существа из его GUID (0xF130 | entry&lt;&lt;24 | counter).</summary>
    private static uint CreatureEntry(ulong guid) => (uint)((guid >> 24) & 0xFFFFFF);

    [WorldOpcodeHandler(WorldOpcode.CmsgListInventory)]
    public async Task OnListInventory(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => await SendVendorListAsync(session, packet.Reader().UInt64(), ct);

    /// <summary>Шлёт окно товаров вендора (SMSG_LIST_INVENTORY), если у NPC есть ассортимент. M6.2.
    /// Вынесено для переиспользования из госсипа квестов (M6.5).</summary>
    internal async Task SendVendorListAsync(WorldSession session, ulong vendorGuid, CancellationToken ct)
    {
        var entry = CreatureEntry(vendorGuid);
        IReadOnlyList<VendorItem> items;
        try { items = await worldDb.GetVendorItemsAsync(entry, ct); }
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
    public async Task OnBuyItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
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
        try { items = await worldDb.GetVendorItemsAsync(entry, ct); }
        catch { await FailAsync(BuyResult.CantFindItem); return; }

        var vi = items.FirstOrDefault(x => x.ItemId == itemEntry);
        if (vi is null) { await FailAsync(BuyResult.CantFindItem); return; }

        // amount — число «лотов»; цена за лот = BuyPrice, предметов в лоте = BuyCount.
        var buyCount = vi.BuyCount == 0 ? 1u : vi.BuyCount;
        var qty = amount * buyCount;          // сколько физических предметов выдать
        var cost = vi.BuyPrice * amount;
        if (session.Inv.Money < cost) { await FailAsync(BuyResult.NotEnoughMoney); return; }

        var ownerGuid = session.InWorldGuid;
        // M7 #20: выдать со стаканием в существующие стопки (как лут/награда). null → нет места.
        // Деньги списываем только после успешной выдачи.
        var placed = await inventoryGrant.TryGiveAsync(session, itemEntry, qty, ct);
        if (placed is null) { await FailAsync(BuyResult.InventoryFull); return; }

        session.Inv.Money -= cost;
        await characters.SetMoneyAsync(ownerGuid, session.Inv.Money, ct);

        // Обновить деньги, подтвердить покупку (предмет/стопку уже создал TryGiveAsync).
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildCoinageUpdate(ownerGuid, session.Inv.Money), ct);

        var buy = new ByteWriter(20)
            .UInt64(vendorGuid)
            .UInt32(vi.Slot + 1)
            .UInt32(vi.MaxCount == 0 ? 0xFFFFFFFFu : vi.MaxCount)
            .UInt32(qty);
        await session.SendAsync(WorldOpcode.SmsgBuyItem, buy.ToArray(), ct);
        session.Logger.LogInformation("BUY '{User}': item={Item} x{Qty} (лотов {Amt}) за {Cost}, осталось {Money}",
            session.Account, itemEntry, qty, amount, cost, session.Inv.Money);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgSellItem)]
    public async Task OnSellItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var vendorGuid = reader.UInt64();
        var itemGuid = reader.UInt64();
        // u8 amount — продаём всю стопку (стартовые предметы не стопкуются).

        async Task FailAsync(SellResult r) => await session.SendAsync(WorldOpcode.SmsgSellItem,
            new ByteWriter(17).UInt64(vendorGuid).UInt64(itemGuid).UInt8((byte)r).ToArray(), ct);

        var itemLow = (uint)(itemGuid & 0xFFFFFFFF);
        var item = session.Inv.Inventory.FirstOrDefault(i => i.ItemGuid == itemLow);
        if (item is null) { await FailAsync(SellResult.CantFindItem); return; }

        ItemTemplateData? t;
        try { t = await worldDb.GetItemTemplateAsync(item.ItemEntry, ct); }
        catch { await FailAsync(SellResult.CantFindItem); return; }
        if (t is null || t.SellPrice == 0) { await FailAsync(SellResult.CantSellItem); return; }

        var ownerGuid = session.InWorldGuid;
        var gain = t.SellPrice * Math.Max(1u, item.StackCount);

        await items.RemoveItemAsync(itemLow, ct);
        session.Inv.Inventory.Remove(item);
        session.Inv.Money += gain;
        await characters.SetMoneyAsync(ownerGuid, session.Inv.Money, ct);

        // Убрать предмет у клиента, очистить слот, обновить деньги.
        await session.SendAsync(WorldOpcode.SmsgDestroyObject,
            new ByteWriter(9).UInt64(ItemObject.ItemGuid(itemLow)).UInt8(0).ToArray(), ct);
        if (item.Bag == InventorySlots.MainBag)
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                PlayerSpawn.BuildInvSlotUpdate(ownerGuid, item.Slot, 0), ct);
        else if (InventorySlots.IsBagSlot(item.Bag)) // M6.13: продажа из надетой сумки — очистить её слот
        {
            var bagGuid = BagInventory.BagGuid(session, item.Bag);
            if (bagGuid != 0)
                await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                    ContainerObject.BuildSlotUpdate(bagGuid, item.Slot, 0), ct);
        }
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildCoinageUpdate(ownerGuid, session.Inv.Money), ct);
        session.Logger.LogInformation("SELL '{User}': item={Item} (guid={Guid}) за {Gain}, теперь {Money}",
            session.Account, item.ItemEntry, itemLow, gain, session.Inv.Money);
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
