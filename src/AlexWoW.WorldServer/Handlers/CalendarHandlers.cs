using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Минимальная поддержка календаря. Полноценный календарь не реализован; отвечаем на запрос
/// числа ожидающих приглашений (<see cref="WorldOpcode.CmsgCalendarGetNumPending"/>) значением 0,
/// чтобы индикатор на часах миникарты не висел в неопределённом состоянии и опкод не логировался
/// как необработанный. (Сам taint Blizzard-аддонов лечится ответом SMSG_ADDON_INFO в AuthHandlers.)
/// </summary>
public static class CalendarHandlers
{
    [WorldOpcodeHandler(WorldOpcode.CmsgCalendarGetNumPending)]
    public static async Task OnGetNumPending(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var w = new ByteWriter(4).UInt32(0); // pending_events = 0
        await session.SendAsync(WorldOpcode.SmsgCalendarSendNumPending, w.ToArray(), ct);
    }
}
