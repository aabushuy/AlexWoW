using AlexWoW.Database.Abstractions;
using AlexWoW.DataStores.Terrain;
using AlexWoW.WorldServer.Handlers;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlexWoW.WorldServer.Net;

/// <summary>
/// Parameter object зависимостей <see cref="WorldSession"/> (M7 #35): собирается DI один раз и передаётся
/// фабрикой в каждую сессию — без service locator. Репозитории здесь временно (мост до S9 #43); из игровых
/// сервисов осталось только нужное самой сессии (роутер, handshake, персист аур, мир, опции, логгер) —
/// мосты dev-командам сняты в M7 S8 (команды получают сервисы ctor-инъекцией).
/// </summary>
internal sealed class WorldSessionServices(
    IOptions<WorldServerOptions> options,
    IAccountRepository accounts,
    ICharacterRepository characters,
    IInventoryRepository items,
    IQuestRepository quests,
    ICharacterStateRepository charState,
    ITeleportRepository teleports,
    IWorldRepository worldDb,
    TerrainMaps terrain,
    WorldState world,
    WorldPacketRouter router,
    AuthChallengeSender authChallenge,
    AuraPersistenceService auraPersistence,
    ILogger<WorldSession> logger)
{
    public WorldServerOptions Options { get; } = options.Value;
    public IAccountRepository Accounts { get; } = accounts;
    public ICharacterRepository Characters { get; } = characters;
    public IInventoryRepository Items { get; } = items;
    public IQuestRepository Quests { get; } = quests;
    public ICharacterStateRepository CharState { get; } = charState;
    public ITeleportRepository Teleports { get; } = teleports;
    public IWorldRepository WorldDb { get; } = worldDb;
    public TerrainMaps Terrain { get; } = terrain;
    public WorldState World { get; } = world;
    public WorldPacketRouter Router { get; } = router;
    public AuthChallengeSender AuthChallenge { get; } = authChallenge;
    // Нужно самой сессии: персист временных аур при выходе из мира (M10.5).
    public AuraPersistenceService AuraPersistence { get; } = auraPersistence;
    public ILogger<WorldSession> Logger { get; } = logger;
}
