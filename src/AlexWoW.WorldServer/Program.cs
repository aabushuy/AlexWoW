using AlexWoW.Database;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Repositories;
using AlexWoW.DataStores.Collision;
using AlexWoW.DataStores.Navigation;
using AlexWoW.DataStores.Terrain;
using AlexWoW.WorldServer;
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
// Срез 4 рефактора DAL (#23): персонажи на EF. EfCharacterStore поверх пул-фабрики AuthDbContext
// (зарегистрирована выше). Фасад + сегрегированные интерфейсы — алиасы на один singleton.
builder.Services.AddSingleton<EfCharacterStore>();
builder.Services.AddSingleton<ICharacterStore>(sp => sp.GetRequiredService<EfCharacterStore>());
builder.Services.AddSingleton<ICharacterRepository>(sp => sp.GetRequiredService<EfCharacterStore>());
builder.Services.AddSingleton<IInventoryRepository>(sp => sp.GetRequiredService<EfCharacterStore>());
builder.Services.AddSingleton<IQuestRepository>(sp => sp.GetRequiredService<EfCharacterStore>());
builder.Services.AddSingleton<ICharacterStateRepository>(sp => sp.GetRequiredService<EfCharacterStore>());
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<WorldServerOptions>>().Value;
    return new WorldDatabase(options.WorldConnectionString);
});
builder.Services.AddSingleton<IWorldRepository>(sp => sp.GetRequiredService<WorldDatabase>());
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
