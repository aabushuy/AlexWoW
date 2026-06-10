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
    SpellCatalog spellCatalog,
    AuraService auras,
    AuraPersistenceService auraPersistence,
    InventoryGrantService inventoryGrant,
    QuestProgressService questProgress,
    SkillsService skills,
    SpellLearnService spellLearn,
    ProgressionService progression,
    TalentHandlers talents,
    TrainerCatalogService trainerCatalog,
    StartingGearService startingGear,
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
    // Спелл-кластер (M7 S3): сервисы для самой сессии (персист аур при выходе) и мосты легаси-статикам.
    public SpellCatalog SpellCatalog { get; } = spellCatalog;
    public AuraService AuraService { get; } = auras;
    public AuraPersistenceService AuraPersistence { get; } = auraPersistence;
    // Квест/лут-кластер (M7 S5): мосты легаси-статикам до их конверсии.
    public InventoryGrantService InventoryGrant { get; } = inventoryGrant;   // мост до S7/S8 (GO-сбор, dev)
    public QuestProgressService QuestProgress { get; } = questProgress;      // мост до S7 (WorldEntryHandlers)
    // Инвентарь/тренеры/прогрессия (M7 S6): мосты легаси-статикам до их конверсии.
    public SkillsService Skills { get; } = skills;                           // мост до S7/S8 (вход в мир, GO-сбор, dev)
    public SpellLearnService SpellLearn { get; } = spellLearn;               // мост до S8 (.learn)
    public ProgressionService Progression { get; } = progression;            // мост до S7/S8 (вход в мир, dev)
    public TalentHandlers Talents { get; } = talents;                        // мост до S7/S8 (вход в мир, dev)
    public TrainerCatalogService TrainerCatalog { get; } = trainerCatalog;   // мост до S8 (.learnall)
    public StartingGearService StartingGear { get; } = startingGear;         // мост до S7 (вход в мир)
    public ILogger<WorldSession> Logger { get; } = logger;
}
