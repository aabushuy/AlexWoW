using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Опкод-входы инвентаря (M6.9/M6.13, DI-модуль M7 S6 — бывший статик InventoryHandlers): тонкий парсинг
/// CMSG-пакетов и делегирование в <see cref="InventoryMoveService"/>. Валидация контейнеров клиентского
/// ввода — на входе (сервер авторитетен).
/// </summary>
internal sealed class InventoryOpcodeHandlers(InventoryMoveService move) : IOpcodeHandlerModule
{
    [WorldOpcodeHandler(WorldOpcode.CmsgSwapInvItem)]
    public async Task OnSwapInvItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        // Порядок байт — destination_slot, ПОТОМ source_slot (CMaNGOS `recv_data >> dstslot >> srcslot`).
        // Оба слота — в основном контейнере (bag=255). M7 #18.
        var r = packet.Reader();
        int dst = r.UInt8(), src = r.UInt8();
        session.Logger.LogDebug("[inv] SWAP_INV src={Src} dst={Dst}", src, dst);
        await move.MoveOrSwapAsync(session, InventorySlots.MainBag, src, InventorySlots.MainBag, dst, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgSwapItem)]
    public async Task OnSwapItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int dstBag = r.UInt8(), dst = r.UInt8(), srcBag = r.UInt8(), src = r.UInt8();
        session.Logger.LogDebug("[inv] SWAP_ITEM srcBag={SB} src={S} dstBag={DB} dst={D}", srcBag, src, dstBag, dst);
        if (!ValidContainer(session, srcBag) || !ValidContainer(session, dstBag)) return;
        await move.MoveOrSwapAsync(session, srcBag, src, dstBag, dst, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAutostoreBagItem)]
    public async Task OnAutostoreBagItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int srcBag = r.UInt8(), src = r.UInt8(), dstBag = r.UInt8();
        session.Logger.LogDebug("[inv] AUTOSTORE srcBag={SB} src={S} dstBag={DB}", srcBag, src, dstBag);
        if (!ValidContainer(session, srcBag) || !ValidContainer(session, dstBag)) return;
        var free = BagInventory.FreeSlotIn(session, dstBag);
        if (free >= 0) await move.MoveOrSwapAsync(session, srcBag, src, dstBag, free, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAutoEquipItem)]
    public async Task OnAutoEquipItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int srcBag = r.UInt8(), src = r.UInt8();
        session.Logger.LogDebug("[inv] AUTOEQUIP srcBag={SB} src={S}", srcBag, src);
        if (!ValidContainer(session, srcBag)) return;
        await move.AutoEquipAsync(session, srcBag, src, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAutoEquipItemSlot)]
    public async Task OnAutoEquipItemSlot(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var itemGuid = r.UInt64();
        int dst = r.UInt8();
        var low = (uint)(itemGuid & 0xFFFFFFFF);
        var item = session.Inventory.FirstOrDefault(i => i.ItemGuid == low);
        if (item is null) return;
        await move.MoveOrSwapAsync(session, item.Bag, item.Slot, InventorySlots.MainBag, dst, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgSplitItem)]
    public async Task OnSplitItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int srcBag = r.UInt8(), src = r.UInt8(), dstBag = r.UInt8(), dst = r.UInt8();
        var amount = r.UInt32();
        if (!ValidContainer(session, srcBag) || !ValidContainer(session, dstBag)) return;
        await move.SplitAsync(session, srcBag, src, dstBag, dst, amount, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgDestroyItem)]
    public async Task OnDestroyItem(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        int bag = r.UInt8(), slot = r.UInt8();
        var amount = r.UInt8();
        if (!ValidContainer(session, bag)) return;
        await move.DestroyAsync(session, bag, slot, amount, ct);
    }

    /// <summary>Допустимый контейнер для адресации: осн. (255) или существующая надетая сумка (19..22). M6.13.</summary>
    private static bool ValidContainer(WorldSession session, int bag)
        => bag == InventorySlots.MainBag
           || (InventorySlots.IsBagSlot(bag) && BagInventory.MainItemAt(session, bag) is not null);
}
