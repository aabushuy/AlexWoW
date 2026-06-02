using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>Движение (M4): все MSG_MOVE_* несут packed guid + MovementInfo — извлекаем позицию.</summary>
public static class MovementHandlers
{
    [WorldOpcodeHandler(
        WorldOpcode.MsgMoveStartForward, WorldOpcode.MsgMoveStartBackward, WorldOpcode.MsgMoveStop,
        WorldOpcode.MsgMoveStartStrafeLeft, WorldOpcode.MsgMoveStartStrafeRight, WorldOpcode.MsgMoveStopStrafe,
        WorldOpcode.MsgMoveJump, WorldOpcode.MsgMoveStartTurnLeft, WorldOpcode.MsgMoveStartTurnRight,
        WorldOpcode.MsgMoveStopTurn, WorldOpcode.MsgMoveStartPitchUp, WorldOpcode.MsgMoveStartPitchDown,
        WorldOpcode.MsgMoveStopPitch, WorldOpcode.MsgMoveSetRunMode, WorldOpcode.MsgMoveSetWalkMode,
        WorldOpcode.MsgMoveFallLand, WorldOpcode.MsgMoveStartSwim, WorldOpcode.MsgMoveStopSwim,
        WorldOpcode.MsgMoveSetFacing, WorldOpcode.MsgMoveSetPitch, WorldOpcode.MsgMoveHeartbeat)]
    public static Task OnMovement(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        try
        {
            var reader = packet.Reader();
            reader.PackedGuid();   // mover guid
            reader.UInt32();       // movement flags
            reader.UInt16();       // movement flags 2
            reader.UInt32();       // time
            session.PosX = reader.Single();
            session.PosY = reader.Single();
            session.PosZ = reader.Single();
            session.PosO = reader.Single();
        }
        catch (InvalidOperationException)
        {
            // Нестандартный вариант пакета — игнорируем для трекинга позиции.
        }
        return Task.CompletedTask;
    }
}
