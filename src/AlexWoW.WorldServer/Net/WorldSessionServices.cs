using AlexWoW.Database.Abstractions;
using AlexWoW.DataStores.Terrain;
using AlexWoW.WorldServer.Handlers;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlexWoW.WorldServer.Net;

/// <summary>
/// Parameter object зависимостей <see cref="WorldSession"/> (M7 #35): собирается DI один раз и передаётся
/// фабрикой в каждую сессию — без service locator. Репозитории здесь временно (мост до S9 #43): легаси-статики
/// читают их через свойства сессии; после конверсии модулей останется только нужное самой сессии
/// (роутер, handshake, сохранение позиции, мир, опции, логгер).
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
    public ILogger<WorldSession> Logger { get; } = logger;
}
