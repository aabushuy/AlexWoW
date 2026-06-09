using System.Collections.Concurrent;
using AlexWoW.Common.Network;
using AlexWoW.DataStores.Navigation;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Хаб состояния мира (singleton): реестр онлайн-игроков и авторитетных существ, пространственные
/// запросы (видимость/радиус) и рассылка наблюдателям. Высокоуровневую логику держат SRP-коллабораторы
/// (рефактор #30): <see cref="PlayerVisibility"/> (вход/выход/видимость игроков), <see cref="CreatureDirector"/>
/// (движение/респавн/манекен существ), <see cref="WorldTick"/> (серверный тик). WorldState их композирует
/// и делегирует — API для потребителей (хендлеры/сессии) не меняется. Доступ из множества потоков —
/// потокобезопасные словари; сериализация отправки — на уровне сессии (WorldSession.SendAsync).
/// </summary>
public sealed class WorldState
{
    private readonly FactionStore _factions;
    private readonly QuestStore _quests;
    private readonly LevelStore _levels;
    private readonly StatStore _stats;

    private readonly PlayerVisibility _visibility;
    private readonly CreatureDirector _director;
    private readonly WorldTick _tick;

    public WorldState(ILogger<WorldState> logger, Navmesh navmesh, FactionStore factions,
        QuestStore quests, LevelStore levels, StatStore stats)
    {
        _factions = factions;
        _quests = quests;
        _levels = levels;
        _stats = stats;
        _director = new CreatureDirector(this, navmesh, logger);
        _visibility = new PlayerVisibility(this, logger);
        _tick = new WorldTick(this, _director, factions, logger);
    }

    // --- Фасады сторов / фракции ---

    /// <summary>Характеристики по расе/классу/уровню (HP/мана/статы). M9.2.</summary>
    public StatStore Stats => _stats;

    /// <summary>Реестр квест-связей (иконки !/?). M6.5.</summary>
    public QuestStore Quests => _quests;

    /// <summary>Прогрессия (кривая XP + XP за килл). M9.1.</summary>
    public LevelStore Levels => _levels;

    /// <summary>Враждебна ли фракция существа к фракции игрока (авто-агро M6.7).</summary>
    public bool IsHostile(uint creatureFactionTemplate, uint playerFactionTemplate)
        => _factions.IsHostile(creatureFactionTemplate, playerFactionTemplate);

    /// <summary>Радиус видимости (ярды). Грубо — как дефолтная зона интереса в WoW.</summary>
    public const float VisibilityRange = 100f;

    /// <summary>Через сколько мс после смерти существо респавнится (тест). M6.3.</summary>
    private const long RespawnDelayMs = 30_000;

    /// <summary>Длительность респавна существа (мс). M6.3.</summary>
    public static long RespawnDelay => RespawnDelayMs;

    // --- Реестр игроков и существ ---

    private readonly ConcurrentDictionary<ulong, WorldPlayer> _players = new();

    /// <summary>
    /// Авторитетные существа по GUID — общие для всех наблюдателей (здоровье/смерть/респавн). M6.3.
    /// Лениво создаются из тех же DB-строк, что и видимость (см. <see cref="GetOrAddCreature"/>).
    /// Пока без эвикции (статические спавны мира; чистку добавим в M6.8).
    /// </summary>
    private readonly ConcurrentDictionary<ulong, WorldCreature> _creatures = new();

    /// <summary>Все онлайн-игроки.</summary>
    public IEnumerable<WorldPlayer> Players => _players.Values;

    /// <summary>Все материализованные существа.</summary>
    public IEnumerable<WorldCreature> Creatures => _creatures.Values;

    /// <summary>Число онлайн-игроков.</summary>
    public int PlayerCount => _players.Count;

    /// <summary>Существо по GUID (или null, если ещё не материализовано). M6.3.</summary>
    public WorldCreature? FindCreature(ulong guid)
        => _creatures.TryGetValue(guid, out var c) ? c : null;

    /// <summary>Онлайн-игрок по GUID (или null). M6.7.</summary>
    public WorldPlayer? FindPlayer(ulong guid)
        => _players.TryGetValue(guid, out var p) ? p : null;

    /// <summary>Берёт существо из реестра или создаёт его лениво (одно на GUID для всех). M6.3.</summary>
    public WorldCreature GetOrAddCreature(ulong guid, Func<WorldCreature> factory)
        => _creatures.GetOrAdd(guid, _ => factory());

    /// <summary>Удаляет существо из реестра (снятие dev-сущности: <c>.trainer off</c>/<c>.devclean</c>). D1.</summary>
    public bool RemoveCreature(ulong guid) => _creatures.TryRemove(guid, out _);

    /// <summary>Монотонный счётчик спавнов dev-сущностей. База в верхней части 24-битного counter'а
    /// (выше реальных creature.guid из дампа) — чтобы GUID dev-спавна не сталкивался с реальным. D1.</summary>
    private int _devSpawnCounter;

    /// <summary>Уникальный counter для GUID dev-спавна (см. <see cref="Npcs.UnitGuid"/>). D1.</summary>
    public uint NextDevSpawnCounter()
        => 0xDE0000u | (uint)(System.Threading.Interlocked.Increment(ref _devSpawnCounter) & 0xFFFF);

    /// <summary>Регистрирует онлайн-игрока (вход в мир). #30.</summary>
    public void RegisterPlayer(WorldPlayer player) => _players[player.Guid] = player;

    /// <summary>Снимает игрока с регистрации (выход). Возвращает true, если был зарегистрирован. #30.</summary>
    public bool UnregisterPlayer(ulong guid) => _players.TryRemove(guid, out _);

    // --- Пространственные запросы ---

    /// <summary>Игроки на той же карте в радиусе видимости (исключая самого center).</summary>
    public IEnumerable<WorldPlayer> PlayersInRangeOf(WorldPlayer center)
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

    /// <summary>Наблюдатели существа: игроки на его карте, у кого оно в видимом наборе. M6.3.</summary>
    public IEnumerable<WorldPlayer> ObserversOf(WorldCreature creature)
        => _players.Values.Where(p => p.Map == creature.Map && p.Session.VisibleNpcs.ContainsKey(creature.Guid));

    // --- Рассылка наблюдателям ---

    /// <summary>
    /// Рассылка пакета соседним игрокам (в радиусе, БЕЗ самого игрока). M6.4: SMSG_SPELL_GO шлётся
    /// наблюдателям, а не кастеру — кастер ведёт каст клиентским предсказанием, и GO, присланный ему,
    /// трактуется как чужой каст (анимация залипает, клиент шлёт CANCEL_CAST).
    /// </summary>
    public async Task BroadcastToNeighborsAsync(WorldPlayer player, WorldOpcode opcode, byte[] body, CancellationToken ct)
    {
        foreach (var other in PlayersInRangeOf(player))
            await other.Session.SendAsync(opcode, body, ct);
    }

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

    /// <summary>Наблюдатели игрока для боя/HP: сам игрок + соседи в радиусе. M6.7.</summary>
    public async Task BroadcastToPlayerObserversAsync(WorldPlayer player, WorldOpcode opcode, byte[] body, CancellationToken ct)
    {
        await player.Session.SendAsync(opcode, body, ct);
        foreach (var other in PlayersInRangeOf(player))
            await other.Session.SendAsync(opcode, body, ct);
    }

    /// <summary>Рассылка текущего HP игрока (VALUES UNIT_FIELD_HEALTH) себе и соседям. M6.7.</summary>
    public Task BroadcastPlayerHealthAsync(WorldPlayer player, CancellationToken ct)
        => BroadcastToPlayerObserversAsync(player, WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate(player.Guid,
                m => m.SetUInt32(UpdateField.UnitHealth, player.Session.Health)), ct);

    // --- Урон (мутация авторитетного состояния) ---

    /// <summary>
    /// Применяет урон существу (общий путь для мили M6.3 и спеллов M6.4): уменьшает HP, на смерти
    /// ставит таймер респавна. НЕ рассылает (порядок с combat-log контролирует вызывающий —
    /// см. <see cref="BroadcastCreatureHealthAsync"/>). Возвращает фактический урон, овёркилл и факт смерти.
    /// </summary>
    public (uint Dealt, uint Overkill, bool Died) ApplyCreatureDamage(WorldCreature creature, uint damage)
    {
        var before = creature.Health;
        var dealt = Math.Min(damage, before);
        creature.Health = before - dealt;
        if (creature.Health == 0 && creature.RespawnAtMs is null)
            creature.RespawnAtMs = Environment.TickCount64 + RespawnDelayMs;
        return (dealt, damage - dealt, creature.Health == 0);
    }

    /// <summary>
    /// Урон игроку (от существа, M6.7): уменьшает авторитетный HP. Возвращает фактический урон и
    /// факт смерти. Рассылку combat-log/HP и обработку смерти делает вызывающий (CombatHandlers).
    /// </summary>
    public (uint Dealt, bool Died) ApplyPlayerDamage(WorldPlayer player, uint damage)
    {
        var s = player.Session;
        var dealt = Math.Min(damage, s.Health);
        s.Health -= dealt;
        return (dealt, s.Health == 0);
    }

    // --- Делегации к SRP-коллабораторам (API не меняется для потребителей) ---

    /// <inheritdoc cref="PlayerVisibility.EnterWorldAsync"/>
    public Task EnterWorldAsync(WorldPlayer player, CancellationToken ct)
        => _visibility.EnterWorldAsync(player, ct);

    /// <inheritdoc cref="PlayerVisibility.RefreshVisiblePlayersAsync"/>
    public Task RefreshVisiblePlayersAsync(WorldPlayer me, CancellationToken ct)
        => _visibility.RefreshVisiblePlayersAsync(me, ct);

    /// <inheritdoc cref="PlayerVisibility.ResendNearbyEquipmentToAsync"/>
    public Task ResendNearbyEquipmentToAsync(WorldPlayer me, CancellationToken ct)
        => _visibility.ResendNearbyEquipmentToAsync(me, ct);

    /// <inheritdoc cref="PlayerVisibility.LeaveWorldAsync"/>
    public Task LeaveWorldAsync(WorldPlayer player, CancellationToken ct)
        => _visibility.LeaveWorldAsync(player, ct);

    /// <inheritdoc cref="PlayerVisibility.RelayMovementAsync"/>
    public Task RelayMovementAsync(WorldPlayer mover, WorldOpcode opcode, byte[] body,
        uint moverTime, int timeFieldOffset, CancellationToken ct)
        => _visibility.RelayMovementAsync(mover, opcode, body, moverTime, timeFieldOffset, ct);

    /// <inheritdoc cref="CreatureDirector.MoveCreatureAsync"/>
    public Task MoveCreatureAsync(WorldCreature creature, float nx, float ny, float nz, uint durationMs, CancellationToken ct)
        => _director.MoveCreatureAsync(creature, nx, ny, nz, durationMs, ct);

    /// <inheritdoc cref="CreatureDirector.FaceCreatureAsync"/>
    public Task FaceCreatureAsync(WorldCreature creature, ulong targetGuid, CancellationToken ct)
        => _director.FaceCreatureAsync(creature, targetGuid, ct);

    /// <inheritdoc cref="CreatureDirector.FindGroundPath"/>
    public IReadOnlyList<(float X, float Y, float Z)>? FindGroundPath(uint map,
        float sx, float sy, float sz, float ex, float ey, float ez)
        => _director.FindGroundPath(map, sx, sy, sz, ex, ey, ez);

    /// <inheritdoc cref="CreatureDirector.SummonTrainingDummyAsync"/>
    public Task SummonTrainingDummyAsync(WorldSession session, CancellationToken ct)
        => _director.SummonTrainingDummyAsync(session, ct);

    /// <inheritdoc cref="CreatureDirector.SummonDevNpcAsync"/>
    public Task<bool> SummonDevNpcAsync(WorldSession session, uint entry, string slot, CancellationToken ct)
        => _director.SummonDevNpcAsync(session, entry, slot, ct);

    /// <inheritdoc cref="CreatureDirector.DespawnDevNpcAsync"/>
    public Task<bool> DespawnDevNpcAsync(WorldSession session, string slot, CancellationToken ct)
        => _director.DespawnDevNpcAsync(session, slot, ct);

    /// <inheritdoc cref="CreatureDirector.SummonDevGoAsync"/>
    public Task<bool> SummonDevGoAsync(WorldSession session, uint entry, string slot, CancellationToken ct)
        => _director.SummonDevGoAsync(session, entry, slot, ct);

    /// <inheritdoc cref="CreatureDirector.DevCleanGosAsync"/>
    public Task DevCleanGosAsync(WorldSession session, CancellationToken ct)
        => _director.DevCleanGosAsync(session, ct);

    /// <inheritdoc cref="CreatureDirector.DevCleanAsync"/>
    public Task DevCleanAsync(WorldSession session, CancellationToken ct)
        => _director.DevCleanAsync(session, ct);

    /// <inheritdoc cref="WorldTick.UpdateAsync"/>
    public Task UpdateAsync(CancellationToken ct) => _tick.UpdateAsync(ct);
}
