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

    /// <summary>
    /// Игрок вошёл в мир: регистрируем и спавним с соседями. Соседи (уже загружены) надёжно видят
    /// новичка сразу; новичку соседи шлются немедленно, но НЕ помечаются видимыми — на первом его
    /// движении (клиент уже догрузил мир) <see cref="RefreshVisiblePlayersAsync"/> пере-создаст их
    /// надёжно (иначе клиент в момент загрузки теряет экипировку чужих — «голые соседи»).
    /// </summary>
    public async Task EnterWorldAsync(WorldPlayer player, CancellationToken ct)
    {
        _players[player.Guid] = player;

        foreach (var other in PlayersInRangeOf(player))
        {
            if (other.Session.VisiblePlayers.TryAdd(player.Guid, 1))
            {
                logger.LogDebug("[vis] CREATE '{P}'(eq={N}) → '{O}' (enter)",
                    player.Character.Name, EquipCount(player), other.Character.Name);
                await other.Session.SendAsync(WorldOpcode.SmsgUpdateObject, BuildPlayerCreate(player), ct);
            }
            logger.LogInformation("[vis] CREATE '{O}'(eq={N}) → '{P}' (enter-self)",
                other.Character.Name, EquipCount(other), player.Character.Name);
            await player.Session.SendAsync(WorldOpcode.SmsgUpdateObject, BuildPlayerCreate(other), ct);
        }

        logger.LogInformation("В мир вошёл '{Name}' (guid={Guid}); онлайн: {Count}",
            player.Character.Name, player.Guid, _players.Count);
    }

    /// <summary>
    /// Пересчёт видимых игроков для текущей позиции (диф): новые в радиусе → CREATE (обоюдно),
    /// ушедшие → DESTROY. Вызывается на движении (клиент загружен) — чинит «голых соседей» и даёт
    /// динамическое появление/исчезновение игроков при перемещении.
    /// </summary>
    public async Task RefreshVisiblePlayersAsync(WorldPlayer me, CancellationToken ct)
    {
        var nearby = PlayersInRangeOf(me).ToList();
        var nearbyGuids = new HashSet<ulong>(nearby.Count);

        foreach (var other in nearby)
        {
            nearbyGuids.Add(other.Guid);
            if (me.Session.VisiblePlayers.TryAdd(other.Guid, 1))
            {
                logger.LogDebug("[vis] CREATE '{Other}'(eq={N}) → '{Me}' (refresh)",
                    other.Character.Name, EquipCount(other), me.Character.Name);
                await me.Session.SendAsync(WorldOpcode.SmsgUpdateObject, BuildPlayerCreate(other), ct);
            }
            // обоюдно: чтобы стоящий на месте сосед тоже увидел подошедшего.
            if (other.Session.VisiblePlayers.TryAdd(me.Guid, 1))
            {
                logger.LogDebug("[vis] CREATE '{Me}'(eq={N}) → '{Other}' (refresh-sym)",
                    me.Character.Name, EquipCount(me), other.Character.Name);
                await other.Session.SendAsync(WorldOpcode.SmsgUpdateObject, BuildPlayerCreate(me), ct);
            }
        }

        foreach (var guid in me.Session.VisiblePlayers.Keys.Where(g => !nearbyGuids.Contains(g)).ToList())
        {
            me.Session.VisiblePlayers.TryRemove(guid, out _);
            await me.Session.SendAsync(WorldOpcode.SmsgDestroyObject,
                new ByteWriter(9).UInt64(guid).UInt8(0).ToArray(), ct);
        }
    }

    /// <summary>
    /// Досылка экипировки соседних игроков этому игроку как VALUES-апдейт (на уже созданные объекты).
    /// Нужна после входа: первый create приходит во время загрузочного экрана, и клиент теряет
    /// видимые предметы соседей; повторный CREATE он игнорирует — применяется именно VALUES-апдейт.
    /// </summary>
    public async Task ResendNearbyEquipmentToAsync(WorldPlayer me, CancellationToken ct)
    {
        foreach (var other in PlayersInRangeOf(me))
        {
            var pkt = PlayerSpawn.BuildEquipmentValuesUpdate(other.Character, other.Session.Inventory);
            if (pkt is not null)
                await me.Session.SendAsync(WorldOpcode.SmsgUpdateObject, pkt, ct);
        }
    }

    /// <summary>Игрок покинул мир: удаляем из реестра и шлём соседям DESTROY (+ чистим их видимость).</summary>
    public async Task LeaveWorldAsync(WorldPlayer player, CancellationToken ct)
    {
        if (!_players.TryRemove(player.Guid, out _))
            return;

        var destroy = new ByteWriter(9).UInt64(player.Guid).UInt8(0).ToArray(); // target_died = 0
        foreach (var other in _players.Values)
        {
            if (other.Guid == player.Guid)
                continue;
            if (other.Session.VisiblePlayers.TryRemove(player.Guid, out _))
                await other.Session.SendAsync(WorldOpcode.SmsgDestroyObject, destroy, ct);
        }

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

    /// <summary>ДИАГНОСТИКА: число надетых видимых предметов (слоты экипировки 0..18).</summary>
    private static int EquipCount(WorldPlayer p)
        => p.Session.Inventory.Count(i => i.Bag == Protocol.InventorySlots.MainBag
            && i.Slot < Protocol.InventorySlots.EquipmentEnd);

    private static byte[] BuildPlayerCreate(WorldPlayer p)
        => PlayerSpawn.BuildCreateObject(p.Character, p.X, p.Y, p.Z, p.O, (uint)Environment.TickCount,
            isSelf: false, p.Session.Inventory);
}
