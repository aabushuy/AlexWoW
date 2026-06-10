using AlexWoW.Database;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Repositories;
using AlexWoW.Database.Repositories.World;
using AlexWoW.DataStores.Collision;
using AlexWoW.DataStores.Navigation;
using AlexWoW.DataStores.Terrain;
using AlexWoW.WorldServer;
using AlexWoW.WorldServer.Handlers;
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
    .WriteTo.Console()
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
// Рефактор #25 (SOLID): WorldDatabase (god-класс) разбит на focused SRP-репозитории (Dapper,
// read-only дамп mangos). *Store зависят от УЗКИХ интерфейсов; WorldSession — от композитного
// фасада IWorldRepository (делегирует этим репозиториям).
static string WorldConn(IServiceProvider sp)
    => sp.GetRequiredService<IOptions<WorldServerOptions>>().Value.WorldConnectionString;
builder.Services.AddSingleton<ICreatureRepository>(sp => new CreatureRepository(WorldConn(sp)));
builder.Services.AddSingleton<IGameObjectRepository>(sp => new GameObjectRepository(WorldConn(sp)));
builder.Services.AddSingleton<IItemTemplateRepository>(sp => new ItemTemplateRepository(WorldConn(sp)));
builder.Services.AddSingleton<IVendorRepository>(sp => new VendorRepository(WorldConn(sp)));
builder.Services.AddSingleton<ITrainerRepository>(sp => new TrainerRepository(WorldConn(sp)));
builder.Services.AddSingleton<ILootRepository>(sp => new LootRepository(WorldConn(sp)));
builder.Services.AddSingleton<IQuestTemplateRepository>(sp => new QuestTemplateRepository(WorldConn(sp)));
builder.Services.AddSingleton<IFactionRepository>(sp => new FactionRepository(WorldConn(sp)));
builder.Services.AddSingleton<IPlayerDataRepository>(sp => new PlayerDataRepository(WorldConn(sp)));
builder.Services.AddSingleton<ISpellTemplateRepository>(sp => new SpellTemplateRepository(WorldConn(sp)));
builder.Services.AddSingleton<ITalentRepository>(sp => new TalentRepository(WorldConn(sp)));
builder.Services.AddSingleton<IWorldRepository, WorldRepository>();
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
builder.Services.AddSingleton<AuraService>();
builder.Services.AddSingleton<PeriodicsService>();
builder.Services.AddSingleton<AuraPersistenceService>();
builder.Services.AddSingleton<ManaRegenService>();
builder.Services.AddSingleton<CombatResourcesService>();
builder.Services.AddSingleton<CraftingService>();
// M7 S4: бой — god-класс CombatHandlers разнесён по SRP-сервисам (мили игрока, ИИ существ, реген HP);
// опкод-входы — модуль CombatOpcodeHandlers (регистрируется assembly-сканом выше).
builder.Services.AddSingleton<PlayerMeleeService>();
builder.Services.AddSingleton<CreatureCombatAI>();
builder.Services.AddSingleton<RegenService>();
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
