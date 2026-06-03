using System.Collections.Concurrent;
using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Общее состояние мира: реестр онлайн-игроков и операции видимости
/// (вход/выход в мир, рассылка движения соседям). Один экземпляр на сервер (singleton).
/// Доступ из множества сессий-потоков — отсюда потокобезопасный словарь и сериализация
/// отправки на уровне каждой сессии (см. WorldSession.SendAsync).
/// </summary>
public sealed class WorldState(ILogger<WorldState> logger)
{
    /// <summary>Радиус видимости (ярды). Грубо — как дефолтная зона интереса в WoW.</summary>
    public const float VisibilityRange = 100f;

    private readonly ConcurrentDictionary<ulong, WorldPlayer> _players = new();

    /// <summary>Игрок вошёл в мир: регистрируем и обоюдно спавним с соседями.</summary>
    public async Task EnterWorldAsync(WorldPlayer player, CancellationToken ct)
    {
        _players[player.Guid] = player;

        foreach (var other in PlayersInRangeOf(player))
        {
            await player.Session.SendAsync(WorldOpcode.SmsgUpdateObject, BuildPlayerCreate(other), ct);
            await other.Session.SendAsync(WorldOpcode.SmsgUpdateObject, BuildPlayerCreate(player), ct);
        }

        logger.LogInformation("В мир вошёл '{Name}' (guid={Guid}); онлайн: {Count}",
            player.Character.Name, player.Guid, _players.Count);
    }

    /// <summary>Игрок покинул мир: удаляем из реестра и шлём соседям DESTROY.</summary>
    public async Task LeaveWorldAsync(WorldPlayer player, CancellationToken ct)
    {
        if (!_players.TryRemove(player.Guid, out _))
            return;

        var destroy = new ByteWriter(9).UInt64(player.Guid).UInt8(0).ToArray(); // target_died = 0
        foreach (var other in PlayersInRangeOf(player))
            await other.Session.SendAsync(WorldOpcode.SmsgDestroyObject, destroy, ct);

        logger.LogInformation("Из мира вышел '{Name}' (guid={Guid}); онлайн: {Count}",
            player.Character.Name, player.Guid, _players.Count);
    }

    /// <summary>Ретрансляция пакета движения соседним игрокам (тело уже содержит packed guid мувера).</summary>
    public async Task RelayMovementAsync(WorldPlayer mover, WorldOpcode opcode, byte[] body, CancellationToken ct)
    {
        foreach (var other in PlayersInRangeOf(mover))
            await other.Session.SendAsync(opcode, body, ct);
    }

    /// <summary>Игроки на той же карте в радиусе видимости (исключая самого center).</summary>
    private IEnumerable<WorldPlayer> PlayersInRangeOf(WorldPlayer center)
    {
        foreach (var other in _players.Values)
        {
            if (other.Guid == center.Guid || other.Map != center.Map)
                continue;
            var dx = other.X - center.X;
            var dy = other.Y - center.Y;
            var dz = other.Z - center.Z;
            if (dx * dx + dy * dy + dz * dz <= VisibilityRange * VisibilityRange)
                yield return other;
        }
    }

    private static byte[] BuildPlayerCreate(WorldPlayer p)
        => PlayerSpawn.BuildCreateObject(p.Character, p.X, p.Y, p.Z, p.O, (uint)Environment.TickCount,
            isSelf: false, p.Session.Inventory);
}
