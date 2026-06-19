using AlexWoW.Database;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Repositories;
using AlexWoW.Database.Repositories.World;
using AlexWoW.DataStores.Collision;
using AlexWoW.DataStores.Navigation;
using AlexWoW.DataStores.Terrain;
using AlexWoW.WorldServer;
using AlexWoW.WorldServer.Handlers;
using AlexWoW.WorldServer.Handlers.Dev;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .CreateLogger();
builder.Services.AddSerilog();

builder.Services.Configure<WorldServerOptions>(
    builder.Configuration.GetSection(WorldServerOptions.SectionName));
// Срез 3 рефактора DAL (#23): auth-чтения world-сессии (валидация session key) через EF. Миграции
// в world НЕ запускаются (EnsureSchema зовёт только CharactersDatabase ниже) — мигрирует один auth-сервер.
builder.Services.AddPooledDbContextFactory<AuthDbContext>((sp, o) =>
{
    var options = sp.GetRequiredService<IOptions<WorldServerOptions>>().Value;
    o.UseMySql(options.ConnectionString, ServerVersion.Create(new Version(8, 4, 0), ServerType.MySql));
});
builder.Services.AddSingleton<IAccountRepository, EfAccountRepository>();
// Персонажи на EF, разбито по SRP (#24): focused-репозиторий на свою область поверх пул-фабрики
// AuthDbContext (зарегистрирована выше). Потребители зависят от узких интерфейсов (ISP).
builder.Services.AddSingleton<ICharacterRepository, EfCharacterRepository>();
builder.Services.AddSingleton<IInventoryRepository, EfInventoryRepository>();
builder.Services.AddSingleton<IQuestRepository, EfQuestRepository>();
builder.Services.AddSingleton<ICharacterStateRepository, EfCharacterStateRepository>();
builder.Services.AddSingleton<ITeleportRepository, EfTeleportRepository>(); // Devcommands #79: точки телепорта
builder.Services.AddSingleton<ISpellTestRepository, EfSpellTestRepository>(); // M12 Spell QA: захват проверки заклинаний
// Рефактор #25 (SOLID): WorldDatabase (god-класс) разбит на focused SRP-репозитории (Dapper,
// read-only дамп mangos). *Store зависят от УЗКИХ интерфейсов; WorldSession — от композитного
// фасада IWorldRepository (делегирует этим репозиториям).
static string WorldConn(IServiceProvider sp)
    => sp.GetRequiredService<IOptions<WorldServerOptions>>().Value.WorldConnectionString;
builder.Services.AddSingleton<ICreatureRepository>(sp => new CreatureRepository(WorldConn(sp)));
builder.Services.AddSingleton<IGameObjectRepository>(sp => new GameObjectRepository(WorldConn(sp)));
builder.Services.AddSingleton<IItemTemplateRepository>(sp => new ItemTemplateRepository(WorldConn(sp)));
builder.Services.AddSingleton<IItemSearchRepository>(sp => new ItemSearchRepository(WorldConn(sp))); // окно «Добавить вещь»
builder.Services.AddSingleton<IVendorRepository>(sp => new VendorRepository(WorldConn(sp)));
builder.Services.AddSingleton<ITrainerRepository>(sp => new TrainerRepository(WorldConn(sp)));
builder.Services.AddSingleton<ILootRepository>(sp => new LootRepository(WorldConn(sp)));
builder.Services.AddSingleton<IQuestTemplateRepository>(sp => new QuestTemplateRepository(WorldConn(sp)));
builder.Services.AddSingleton<IFactionRepository>(sp => new FactionRepository(WorldConn(sp)));
builder.Services.AddSingleton<IPlayerDataRepository>(sp => new PlayerDataRepository(WorldConn(sp)));
builder.Services.AddSingleton<ISpellTemplateRepository>(sp => new SpellTemplateRepository(WorldConn(sp)));
builder.Services.AddSingleton<ITalentRepository>(sp => new TalentRepository(WorldConn(sp)));
builder.Services.AddSingleton<IWorldRepository, WorldRepository>();
// KB7: доступ к канбан-доске в БД project (задачи на тестирование для тестировщиков). Отдельная строка подключения.
// KB14: ISpellSchoolRepository подгружает SchoolMask из mangos.spell_template — нужен для сортировки regression-списка по школе.
builder.Services.AddSingleton<ISpellSchoolRepository>(sp => new SpellSchoolRepository(WorldConn(sp)));
builder.Services.AddSingleton<IKanbanBoardRepository>(sp => new KanbanBoardRepository(
    sp.GetRequiredService<IOptions<WorldServerOptions>>().Value.ProjectConnectionString,
    sp.GetRequiredService<ISpellSchoolRepository>()));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<WorldServerOptions>>().Value;
    return new TerrainMaps(options.MapsPath);
});
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<WorldServerOptions>>().Value;
    return new Vmaps(options.VmapsPath);
});
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<WorldServerOptions>>().Value;
    return new Navmesh(options.MmapsPath);
});
builder.Services.AddSingleton<FactionStore>();
builder.Services.AddSingleton<QuestStore>();
builder.Services.AddSingleton<LevelStore>();
builder.Services.AddSingleton<StatStore>();
builder.Services.AddSingleton<AlexWoW.DataStores.CombatRatings>(); // защитные статы (gt-геймтейблы)
builder.Services.AddSingleton<WorldState>();
// Опкод-модули + роутер (M7 #35): модули — DI-синглтоны (скан сборки), роутер собирает их методы
// с [WorldOpcodeHandler] в таблицу. Сессии создаёт фабрика с parameter object (без service locator).
builder.Services.AddWorldOpcodeHandlers();
builder.Services.AddSingleton<AddonProtocol>(); // не модуль (своих опкодов нет) — сервис для ChatHandlers (M7 #36)
// M7 S3: спелл-кластер — статики сконвертированы в stateless DI-синглтоны (данные спеллов, оркестрация
// каста и его завершение, ауры/периодика и их персист, реген ресурсов, переключатели, эффекты, крафт).
builder.Services.AddSingleton<SpellCatalog>();
builder.Services.AddSingleton<SpellGoSender>();
builder.Services.AddSingleton<SpellCastService>();
builder.Services.AddSingleton<SpellCastCompletion>();
builder.Services.AddSingleton<SpellTogglesService>();
builder.Services.AddSingleton<SpellEffectsService>();
builder.Services.AddSingleton<SpellModifierService>(); // M10.6: модификаторы пассивных талантов (ауры 107/108)
builder.Services.AddSingleton<SpellTestCaptureService>();  // M12 Spell QA: рекордер захвата
builder.Services.AddSingleton<SpellTestHarnessService>();  // M12 Spell QA: авто-харнесс прогона абилок
builder.Services.AddSingleton<SpellTestRequestService>();  // QA T1 (Vikunja 185): очередь запросов на авто-прогон (DB-flag + World-tick)
builder.Services.AddSingleton<AuraService>();
builder.Services.AddSingleton<AuraStateService>();  // DEFENSE.1: UNIT_FIELD_AURASTATE + окно Revenge
builder.Services.AddSingleton<PeriodicsService>();
builder.Services.AddSingleton<AuraPersistenceService>();
builder.Services.AddSingleton<ManaRegenService>();
builder.Services.AddSingleton<CombatResourcesService>();
builder.Services.AddSingleton<ComboPointService>(); // Фаза 2 (CP.1): очки серии (combo points) рога/друид-кошки
builder.Services.AddSingleton<RuneService>(); // Фаза 2 (RUNE.1): руны Рыцаря смерти
builder.Services.AddSingleton<CraftingService>();
// M7 S4: бой — god-класс CombatHandlers разнесён по SRP-сервисам (мили игрока, ИИ существ, реген HP);
// опкод-входы — модуль CombatOpcodeHandlers (регистрируется assembly-сканом выше).
builder.Services.AddSingleton<PlayerMeleeService>();
builder.Services.AddSingleton<SealService>(); // Фаза 2: on-hit прок печатей паладина
builder.Services.AddSingleton<ImbueService>(); // §8: on-hit прок оружейных имбу шамана
builder.Services.AddSingleton<PoisonService>(); // §8: on-hit прок ядов разбойника
builder.Services.AddSingleton<CrowdControlService>(); // Фаза 2: контроль (стан/рут/страх/немота)
builder.Services.AddSingleton<AbsorbShieldService>(); // Фаза 2 (ABS.1): absorb-щиты (PW:Shield/Ice Barrier)
builder.Services.AddSingleton<DispelService>(); // Фаза 2 (DSP.1): диспел аур (Cleanse/Remove Curse/Dispel Magic)
builder.Services.AddSingleton<ProcService>(); // Фаза 2 (PROC.1): проки (триггер-спеллы на событии)
builder.Services.AddSingleton<CreatureCombatAI>();
builder.Services.AddSingleton<RegenService>();
// M7 S5: квест/лут-кластер — god-класс QuestHandlers разнесён по SRP-сервисам (прогресс/персист, иконки
// квестгиверов, диалоги/награды, госсип-оркестрация), выдача предметов и награда за убийство — DI-сервисы;
// опкод-входы — модули QuestOpcodeHandlers/LootHandlers (регистрируются assembly-сканом выше).
builder.Services.AddSingleton<QuestProgressService>();
builder.Services.AddSingleton<QuestGiverStatusService>();
builder.Services.AddSingleton<QuestDialogService>();
builder.Services.AddSingleton<GossipService>();
builder.Services.AddSingleton<InventoryGrantService>();
builder.Services.AddSingleton<KillRewardService>();
// M7 S6: инвентарь/вендор/тренер/прогрессия — статики сконвертированы в stateless DI-синглтоны
// (перемещение/сплит/выброс предметов, ресинк клиенту, навыки, изучение спеллов, опыт/уровни, каталог
// тренеров, стартовая экипировка); опкод-входы — модули InventoryOpcodeHandlers/VendorHandlers/
// TrainerHandlers/TalentHandlers (регистрируются assembly-сканом выше).
builder.Services.AddSingleton<InventoryClientSync>();
builder.Services.AddSingleton<InventoryMoveService>();
builder.Services.AddSingleton<SkillsService>();
builder.Services.AddSingleton<SpellLearnService>();
builder.Services.AddSingleton<ProgressionService>();
builder.Services.AddSingleton<TrainerCatalogService>();
builder.Services.AddSingleton<StartingGearService>();
// M7 S7: финал миграции хендлеров — последние статики сконвертированы (оркестрация входа в мир,
// видимость окрестных NPC/GO, синхронизация часов, телепорт); опкод-входы — модули
// WorldEntryOpcodeHandlers/SpawnHandlers/GameObjectUseHandlers (регистрируются assembly-сканом выше).
// Статический фолбэк роутера удалён: остаточный [WorldOpcodeHandler]-статик валит старт.
builder.Services.AddSingleton<VisibilityService>();
builder.Services.AddSingleton<TimeSyncService>();
builder.Services.AddSingleton<LoginSequenceService>();
builder.Services.AddSingleton<TeleportService>();
// M7 S8: dev-подсистема — DI: команды сканом сборки (AddDevCommands: IDevCommand → реестр → диспетчер,
// диспетчер инжектится в ChatHandlers), системные ответы в чат — ChatNotifier (бывший статик Dev.DevChat,
// байты — Protocol/ChatPackets), каталог dev-меню аддона — DevMenuCatalog (потребитель — AddonProtocol).
builder.Services.AddSingleton<ChatNotifier>();
builder.Services.AddSingleton<DevMenuCatalog>();
builder.Services.AddSingleton<DevStatsCatalog>(); // §178: каталог вторичных статов для редактора аддона
builder.Services.AddDevCommands();
builder.Services.AddSingleton<WorldTick>(); // тик мира — DI-синглтон (S3 #5), драйвится WorldUpdateLoop
builder.Services.AddSingleton<AuthChallengeSender>();
builder.Services.AddSingleton<WorldSessionServices>();
builder.Services.AddSingleton<WorldSessionFactory>();
builder.Services.AddHostedService<WorldUpdateLoop>();
builder.Services.AddHostedService<WorldListener>();

var host = builder.Build();

try
{
    Log.Information("AlexWoW WorldServer запускается…");
    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "WorldServer аварийно завершился");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
