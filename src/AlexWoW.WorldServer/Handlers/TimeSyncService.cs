using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Синхронизация часов клиента (M6.3 ч.2, DI-сервис M7 S7 — вынос из легаси-статика WorldEntryHandlers):
/// рассылка SMSG_TIME_SYNC_REQ. Ответ клиента (CMSG_TIME_SYNC_RESP) матчится в
/// <see cref="WorldEntryOpcodeHandlers"/> — там считается дельта часов для нормализации времени движения.
/// </summary>
internal sealed class TimeSyncService
{
    /// <summary>
    /// Шлёт SMSG_TIME_SYNC_REQ (новый счётчик) и запоминает время отправки — фундамент расчёта
    /// дельты часов клиента для нормализации времени движения. Зовётся при входе, после телепорта
    /// и периодически из тика (<see cref="World.WorldTick"/>).
    /// </summary>
    internal async Task SendTimeSyncReqAsync(WorldSession session, CancellationToken ct)
    {
        var counter = session.TimeSyncCounter++;
        session.TimeSyncOutstanding = counter;
        session.TimeSyncSentMs = (uint)Environment.TickCount64;
        session.LastTimeSyncDispatchMs = Environment.TickCount64;
        await session.SendAsync(WorldOpcode.SmsgTimeSyncReq, new ByteWriter(4).UInt32(counter).ToArray(), ct);
    }
}
