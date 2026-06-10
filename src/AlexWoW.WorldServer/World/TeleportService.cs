using AlexWoW.WorldServer.Handlers;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Телепорт игрока в произвольную точку (Devcommands #79). Та же карта — мгновенно
/// (<c>MSG_MOVE_TELEPORT_ACK</c>, как Blink/Shadowstep). Другая карта — <c>SMSG_TRANSFER_PENDING</c> +
/// <c>SMSG_NEW_WORLD</c>; клиент грузит карту и отвечает <c>MSG_MOVE_WORLDPORT_ACK</c>, после чего
/// <see cref="CompleteWorldportAsync"/> пере-ставит игрока в мир на новой карте. SRP — вынесено из
/// хендлеров. Сверено с TrinityCore <c>WorldSession::HandleMoveWorldportAck</c>.
/// </summary>
internal static class TeleportService
{
    /// <summary>Телепортировать игрока в (map,x,y,z,o). Выбирает путь «та же карта»/«кросс-карта».</summary>
    internal static async Task TeleportAsync(WorldSession session, uint map,
        float x, float y, float z, float o, CancellationToken ct)
    {
        if (session.Player is not { } player || session.Character is not { } character)
            return;

        if (map == character.Map)
        {
            // Та же карта — мгновенно: клиент применяет позицию из ACK и отвечает тем же опкодом.
            var guid = (ulong)session.InWorldGuid;
            await session.SendAsync(WorldOpcode.MsgMoveTeleportAck,
                MovementPackets.BuildTeleportAck(guid, session.NextTeleportCounter(), x, y, z, o), ct);
            await session.World.BroadcastToNeighborsAsync(player, WorldOpcode.MsgMoveTeleport,
                MovementPackets.BuildTeleport(guid, x, y, z, o), ct);
            session.PosX = x; session.PosY = y; session.PosZ = z; session.PosO = o;
            await session.Characters.SavePositionAsync(session.InWorldGuid, x, y, z, map, ct);

            // Дальний прыжок по той же карте (напр. Штормград↔Стальгорн, обе map 0): пересчитать окрестности
            // в точке прибытия, иначе NPC/объекты/игроки назначения не появятся, пока игрок не сдвинется.
            await SpawnHandlers.RefreshVisibleNpcsAsync(session, map, x, y, ct);
            await SpawnHandlers.RefreshVisibleGameObjectsAsync(session, map, x, y, ct);
            await session.World.RefreshVisiblePlayersAsync(player, ct);
            session.Logger.LogInformation("TP '{User}' (та же карта {Map}) → ({X:F1};{Y:F1};{Z:F1})",
                session.Account, map, x, y, z);
            return;
        }

        // Кросс-карта: снять с реестра и DESTROY соседям старой карты (клиент выгрузит мир на загрузочном
        // экране), обновить авторитетную позицию+карту, анонсировать переход и загрузить новую карту.
        await session.World.LeaveWorldAsync(player, ct);
        session.VisibleNpcs.Clear();
        session.VisibleGos.Clear();
        session.VisiblePlayers.Clear();

        character.Map = map;
        session.PosX = x; session.PosY = y; session.PosZ = z; session.PosO = o;
        session.LastVisX = 0; session.LastVisY = 0; // форсировать пересчёт видимости после входа
        await session.Characters.SavePositionAsync(session.InWorldGuid, x, y, z, map, ct);

        session.PendingWorldport = true;
        await session.SendAsync(WorldOpcode.SmsgTransferPending, MovementPackets.BuildTransferPending(map), ct);
        await session.SendAsync(WorldOpcode.SmsgNewWorld, MovementPackets.BuildNewWorld(map, x, y, z, o), ct);
        session.Logger.LogInformation("TP '{User}' (кросс-карта → {Map}) → ({X:F1};{Y:F1};{Z:F1}); ждём WORLDPORT_ACK",
            session.Account, map, x, y, z);
    }

    /// <summary>
    /// Завершение кросс-карта телепорта по <c>MSG_MOVE_WORLDPORT_ACK</c>: пере-регистрация в мире на новой
    /// карте (обоюдный спавн с соседями), пересчёт окрестных NPC/гейм-объектов, time sync (возврат
    /// управления). Объект самого игрока клиент сохранил при переходе — self-спавн не нужен.
    /// </summary>
    internal static async Task CompleteWorldportAsync(WorldSession session, CancellationToken ct)
    {
        if (!session.PendingWorldport)
            return;
        session.PendingWorldport = false;

        if (session.Player is not { } player || session.Character is not { } character)
            return;

        await session.World.EnterWorldAsync(player, ct);
        await SpawnHandlers.RefreshVisibleNpcsAsync(session, character.Map, session.PosX, session.PosY, ct);
        await SpawnHandlers.RefreshVisibleGameObjectsAsync(session, character.Map, session.PosX, session.PosY, ct);
        await WorldEntryHandlers.SendTimeSyncReqAsync(session, ct);

        session.Logger.LogInformation("WORLDPORT '{User}' завершён: map={Map} ({X:F1};{Y:F1};{Z:F1})",
            session.Account, character.Map, session.PosX, session.PosY, session.PosZ);
    }
}
