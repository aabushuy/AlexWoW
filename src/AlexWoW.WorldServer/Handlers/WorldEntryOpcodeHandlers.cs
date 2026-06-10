using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Вход/выход из мира и time sync (M4; DI-модуль M7 S7 — опкод-входы бывшего легаси-статика
/// WorldEntryHandlers). Полная последовательность входа — <see cref="LoginSequenceService"/>.
/// </summary>
internal sealed class WorldEntryOpcodeHandlers(LoginSequenceService loginSequence) : IOpcodeHandlerModule
{
    [WorldOpcodeHandler(WorldOpcode.CmsgPlayerLogin)]
    public async Task OnPlayerLogin(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var guid = (uint)reader.UInt64();
        await loginSequence.LoginAsync(session, guid, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgLogoutRequest)]
    public async Task OnLogoutRequest(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        await session.SavePositionIfInWorldAsync(ct);
        await session.LeaveWorldAsync(ct); // снять с реестра + DESTROY соседям

        await session.SendAsync(WorldOpcode.SmsgLogoutResponse,
            new ByteWriter(5).UInt32(0).UInt8(1).ToArray(), ct); // reason=0, instant=1
        await session.SendAsync(WorldOpcode.SmsgLogoutComplete, [], ct);
        session.Logger.LogInformation("LOGOUT '{User}' → возврат к выбору персонажа", session.Account);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgLogoutCancel)]
    public Task OnLogoutCancel(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => session.SendAsync(WorldOpcode.SmsgLogoutCancelAck, [], ct);

    [WorldOpcodeHandler(WorldOpcode.CmsgTimeSyncResp)]
    public Task OnTimeSyncResp(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var counter = reader.UInt32();
        var clientTicks = reader.UInt32();
        // Матчим ответ с последним REQ → дельта часов (serverMs − clientTicks). RTT на LAN пренебрежим.
        if (counter == session.TimeSyncOutstanding)
        {
            session.ClockDeltaMs = session.TimeSyncSentMs - clientTicks;
            session.Logger.LogDebug("[timesync] '{User}': counter={C} clientTicks={T} → delta={D}мс",
                session.Account, counter, clientTicks, session.ClockDeltaMs);
        }
        return Task.CompletedTask;
    }
}
