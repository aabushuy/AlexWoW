using AlexWoW.Database;
using AlexWoW.Database.Abstractions;
using AlexWoW.DataStores.Collision;
using AlexWoW.DataStores.Navigation;
using AlexWoW.DataStores.Terrain;
using AlexWoW.WorldServer;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<WorldServerOptions>>().Value;
    return new AuthDatabase(options.ConnectionString);
});
builder.Services.AddSingleton<IAccountRepository>(sp => sp.GetRequiredService<AuthDatabase>());
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<WorldServerOptions>>().Value;
    return new CharactersDatabase(options.ConnectionString);
});
// Фасад + сегрегированные интерфейсы — алиасы на один singleton CharactersDatabase.
builder.Services.AddSingleton<ICharacterStore>(sp => sp.GetRequiredService<CharactersDatabase>());
builder.Services.AddSingleton<ICharacterRepository>(sp => sp.GetRequiredService<CharactersDatabase>());
builder.Services.AddSingleton<IInventoryRepository>(sp => sp.GetRequiredService<CharactersDatabase>());
builder.Services.AddSingleton<IQuestRepository>(sp => sp.GetRequiredService<CharactersDatabase>());
builder.Services.AddSingleton<ICharacterStateRepository>(sp => sp.GetRequiredService<CharactersDatabase>());
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
