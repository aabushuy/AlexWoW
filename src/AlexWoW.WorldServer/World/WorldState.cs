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

    /// <summary>Через сколько мс после смерти существо респавнится (тест). M6.3.</summary>
    private const long RespawnDelayMs = 30_000;

    /// <summary>Период рассылки SMSG_TIME_SYNC_REQ каждому игроку (нормализация часов). M6.3 ч.2.</summary>
    private const long TimeSyncIntervalMs = 10_000;

    private readonly ConcurrentDictionary<ulong, WorldPlayer> _players = new();

    /// <summary>
    /// Авторитетные существа по GUID — общие для всех наблюдателей (здоровье/смерть/респавн). M6.3.
    /// Лениво создаются из тех же DB-строк, что и видимость (см. <see cref="GetOrAddCreature"/>).
    /// Пока без эвикции (статические спавны мира; чистку добавим в M6.8).
    /// </summary>
    private readonly ConcurrentDictionary<ulong, WorldCreature> _creatures = new();

    /// <summary>Существо по GUID (или null, если ещё не материализовано). M6.3.</summary>
    public WorldCreature? FindCreature(ulong guid)
        => _creatures.TryGetValue(guid, out var c) ? c : null;

    /// <summary>Берёт существо из реестра или создаёт его лениво (одно на GUID для всех). M6.3.</summary>
    public WorldCreature GetOrAddCreature(ulong guid, Func<WorldCreature> factory)
        => _creatures.GetOrAdd(guid, _ => factory());

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

    /// <summary>
    /// Ретрансляция пакета движения соседним игрокам (тело уже содержит packed guid мувера).
    /// M6.3 ч.2: переписывает поле <c>time</c> в часы наблюдателя
    /// (<c>T_obs = T_mover + delta_mover − delta_obs</c>), чтобы убрать сдвиг экстраполяции
    /// между клиентами с разными точками отсчёта тиков. Пока дельта любой из сторон неизвестна —
    /// ретранслируем тело как есть (не хуже прежнего поведения).
    /// </summary>
    public async Task RelayMovementAsync(WorldPlayer mover, WorldOpcode opcode, byte[] body,
        uint moverTime, int timeFieldOffset, CancellationToken ct)
    {
        // Один рерайт в СЕРВЕРНОЕ время (как TrinityCore AdjustClientMovementTime): time += delta_мувера.
        // Все наблюдатели получают одинаковое тело; пока дельта мувера неизвестна — как есть.
        var outBody = body;
        if (timeFieldOffset >= 0 && timeFieldOffset + 4 <= body.Length
            && mover.Session.ClockDeltaMs is { } delta)
        {
            var serverTime = (uint)((long)moverTime + delta);
            outBody = (byte[])body.Clone();
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                outBody.AsSpan(timeFieldOffset, 4), serverTime);
        }

        foreach (var other in PlayersInRangeOf(mover))
            await other.Session.SendAsync(opcode, outBody, ct);
    }

    /// <summary>
    /// Тик мира (M6.3): продвигает мили-свинги атакующих игроков и респавнит мёртвых существ.
    /// Вызывается из <see cref="WorldUpdateLoop"/> ~раз в 250 мс. Исключения по отдельной сессии
    /// не должны валить весь тик — ловим их поштучно.
    /// </summary>
    public async Task UpdateAsync(CancellationToken ct)
    {
        var now = Environment.TickCount64;

        foreach (var player in _players.Values)
        {
            try
            {
                await Handlers.CombatHandlers.TickMeleeAsync(player.Session, now, ct);

                // M6.3 ч.2: периодическая синхронизация часов клиента (для нормализации движения).
                if (now - player.Session.LastTimeSyncDispatchMs >= TimeSyncIntervalMs)
                    await Handlers.WorldEntryHandlers.SendTimeSyncReqAsync(player.Session, ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug("Тик '{User}': {Msg}", player.Character.Name, ex.Message);
            }
        }

        foreach (var creature in _creatures.Values)
        {
            if (creature.IsAlive || creature.RespawnAtMs is not { } at || now < at)
                continue;
            try
            {
                await RespawnCreatureAsync(creature, ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug("Респавн существа {Guid}: {Msg}", creature.Guid, ex.Message);
            }
        }
    }

    /// <summary>Воскрешает существо (полное HP) и шлёт наблюдателям апдейт здоровья. M6.3.</summary>
    private async Task RespawnCreatureAsync(WorldCreature creature, CancellationToken ct)
    {
        creature.Health = creature.MaxHealth;
        creature.RespawnAtMs = null;
        await BroadcastCreatureHealthAsync(creature, ct);
        logger.LogDebug("Существо '{Name}' (guid={Guid}) респавнилось", creature.Template.Name, creature.Guid);
    }

    /// <summary>Наблюдатели существа: игроки на его карте, у кого оно в видимом наборе. M6.3.</summary>
    public IEnumerable<WorldPlayer> ObserversOf(WorldCreature creature)
        => _players.Values.Where(p => p.Map == creature.Map && p.Session.VisibleNpcs.ContainsKey(creature.Guid));

    /// <summary>Рассылка пакета всем наблюдателям существа (бой/смерть/HP). M6.3.</summary>
    public async Task BroadcastToObserversAsync(WorldCreature creature, WorldOpcode opcode, byte[] body, CancellationToken ct)
    {
        foreach (var observer in ObserversOf(creature).ToList())
            await observer.Session.SendAsync(opcode, body, ct);
    }

    /// <summary>Рассылка текущего здоровья существа наблюдателям (VALUES-апдейт UNIT_FIELD_HEALTH). M6.3.</summary>
    public Task BroadcastCreatureHealthAsync(WorldCreature creature, CancellationToken ct)
        => BroadcastToObserversAsync(creature, WorldOpcode.SmsgUpdateObject,
            CreatureUpdate.BuildHealthUpdate(creature.Guid, creature.Health), ct);

    /// <summary>Длительность респавна существа (мс). M6.3.</summary>
    public static long RespawnDelay => RespawnDelayMs;

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
