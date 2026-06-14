using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>Движение (M4): все MSG_MOVE_* несут packed guid + MovementInfo — извлекаем позицию.
/// (DI-модуль, M7 #36; прерывание каста движением — <see cref="SpellCastService"/>, S3;
/// телепорт/видимость — DI-сервисы, S7.)</summary>
internal sealed class MovementHandlers(SpellCastService spellCast, TeleportService teleport, VisibilityService visibility)
    : IOpcodeHandlerModule
{
    /// <summary>MSG_MOVE_TELEPORT_ACK (ответ клиента на телепорт, M7 #33): позиция уже применена сервером —
    /// просто подтверждаем (без обработки), чтобы не было «опкод без обработчика».</summary>
    [WorldOpcodeHandler(WorldOpcode.MsgMoveTeleportAck)]
    public Task OnTeleportAck(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>CMSG_FORCE_MOVE_ROOT_ACK / CMSG_FORCE_MOVE_UNROOT_ACK (IMMUNITY.1): клиент подтвердил рут/анрут
    /// движения (Ice Block) — сервер просто принимает (без валидации счётчика), чтобы не было «опкод без обработчика».</summary>
    [WorldOpcodeHandler(WorldOpcode.CmsgForceMoveRootAck, WorldOpcode.CmsgForceMoveUnrootAck)]
    public Task OnForceMoveAck(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>MSG_MOVE_WORLDPORT_ACK (Devcommands #79): клиент догрузил новую карту после SMSG_NEW_WORLD —
    /// завершаем кросс-карта телепорт (пере-вход в мир, окрестности, time sync).</summary>
    [WorldOpcodeHandler(WorldOpcode.MsgMoveWorldportAck)]
    public Task OnWorldportAck(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => teleport.CompleteWorldportAsync(session, ct);

    [WorldOpcodeHandler(
        WorldOpcode.MsgMoveStartForward, WorldOpcode.MsgMoveStartBackward, WorldOpcode.MsgMoveStop,
        WorldOpcode.MsgMoveStartStrafeLeft, WorldOpcode.MsgMoveStartStrafeRight, WorldOpcode.MsgMoveStopStrafe,
        WorldOpcode.MsgMoveJump, WorldOpcode.MsgMoveStartTurnLeft, WorldOpcode.MsgMoveStartTurnRight,
        WorldOpcode.MsgMoveStopTurn, WorldOpcode.MsgMoveStartPitchUp, WorldOpcode.MsgMoveStartPitchDown,
        WorldOpcode.MsgMoveStopPitch, WorldOpcode.MsgMoveSetRunMode, WorldOpcode.MsgMoveSetWalkMode,
        WorldOpcode.MsgMoveFallLand, WorldOpcode.MsgMoveStartSwim, WorldOpcode.MsgMoveStopSwim,
        WorldOpcode.MsgMoveSetFacing, WorldOpcode.MsgMoveSetPitch, WorldOpcode.MsgMoveHeartbeat)]
    public async Task OnMovement(WorldSession session, IncomingPacket packet, CancellationToken ct)
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
        if (session.Cast.CastingSpellId != 0)
            await spellCast.InterruptOnMoveAsync(session, ct);

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
            var dx = session.PosX - session.Visibility.LastVisX;
            var dy = session.PosY - session.Visibility.LastVisY;
            if (dx * dx + dy * dy >= VisibilityService.VisRefreshStep * VisibilityService.VisRefreshStep)
            {
                await visibility.RefreshVisibleNpcsAsync(session, character.Map, session.PosX, session.PosY, ct);
                await visibility.RefreshVisibleGameObjectsAsync(session, character.Map, session.PosX, session.PosY, ct);
            }
        }
    }
}
