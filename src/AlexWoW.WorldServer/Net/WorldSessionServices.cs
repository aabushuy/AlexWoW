using AlexWoW.Database.Abstractions;
using AlexWoW.DataStores.Terrain;
using AlexWoW.WorldServer.Handlers;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlexWoW.WorldServer.Net;

/// <summary>
/// Parameter object зависимостей <see cref="WorldSession"/> (M7 #35): собирается DI один раз и передаётся
/// фабрикой в каждую сессию — без service locator. Репозитории здесь временно (мост до S9 #43), игровые
/// сервисы — мосты dev-командам (до S8): после их конверсии останется только нужное самой сессии
/// (роутер, handshake, сохранение позиции, персист аур, мир, опции, логгер).
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
    AuraService auras,
    AuraPersistenceService auraPersistence,
    InventoryGrantService inventoryGrant,
    SkillsService skills,
    SpellLearnService spellLearn,
    ProgressionService progression,
    TalentHandlers talents,
    TrainerCatalogService trainerCatalog,
    TeleportService teleport,
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
    // Мосты до S8 (M7 S7): опкод-хендлеры сконвертированы — через сессию сервисы достают
    // только dev-команды (статики до S8).
    public AuraService AuraService { get; } = auras;                          // .buff/.unbuff
    public InventoryGrantService InventoryGrant { get; } = inventoryGrant;    // .additem
    public SkillsService Skills { get; } = skills;                            // .skill
    public SpellLearnService SpellLearn { get; } = spellLearn;                // .learn
    public ProgressionService Progression { get; } = progression;             // .xp/.level
    public TalentHandlers Talents { get; } = talents;                         // .resettalents
    public TrainerCatalogService TrainerCatalog { get; } = trainerCatalog;    // .learnall
    public TeleportService Teleport { get; } = teleport;                      // .tp
    public ILogger<WorldSession> Logger { get; } = logger;
}
