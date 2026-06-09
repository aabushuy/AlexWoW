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
    public static async Task OnMovement(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        // Смещение/значение поля time в теле — для нормализации часов при ретрансляции (M6.3 ч.2).
        // -1 = не распарсили (нестандартный пакет) → ретранслируем как есть.
        var timeFieldOffset = -1;
        uint moverTime = 0;
        try
        {
            var reader = packet.Reader();
            reader.PackedGuid();   // mover guid
            reader.UInt32();       // movement flags
            reader.UInt16();       // movement flags 2
            timeFieldOffset = reader.Position; // time идёт сразу после flags2
            moverTime = reader.UInt32();       // time
            session.PosX = reader.Single();
            session.PosY = reader.Single();
            session.PosZ = reader.Single();
            session.PosO = reader.Single();
        }
        catch (InvalidOperationException)
        {
            // Нестандартный вариант пакета — игнорируем для трекинга позиции.
            timeFieldOffset = -1;
        }

        // M6.4: сдвиг игрока прерывает текущий каст (клиент гасит бар локально, но серверу не шлёт
        // CANCEL_CAST — без этого эффект применился бы и анимация залипала).
        if (session.CastingSpellId != 0)
            await SpellCaster.InterruptOnMoveAsync(session, ct);

        // M5.3: ретранслируем движение соседям (с нормализацией поля time, если часы синхронизированы).
        if (session.Player is { } player)
        {
            await session.World.RelayMovementAsync(player, packet.Opcode, packet.Body, moverTime, timeFieldOffset, ct);
            // M6: видимость игроков — дёшево (в памяти), считаем на каждый пакет движения, чтобы
            // экипировка соседа появлялась сразу при первом шаге (клиент уже догружен).
            await session.World.RefreshVisiblePlayersAsync(player, ct);
        }

        // M5.6: пересчёт видимости NPC/GO (запрос в БД) — троттлим по дистанции.
        if (session.Character is { } character)
        {
            var dx = session.PosX - session.LastVisX;
            var dy = session.PosY - session.LastVisY;
            if (dx * dx + dy * dy >= SpawnHandlers.VisRefreshStep * SpawnHandlers.VisRefreshStep)
            {
                await SpawnHandlers.RefreshVisibleNpcsAsync(session, character.Map, session.PosX, session.PosY, ct);
                await SpawnHandlers.RefreshVisibleGameObjectsAsync(session, character.Map, session.PosX, session.PosY, ct);
            }
        }
    }
}
