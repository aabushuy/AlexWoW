using AlexWoW.AuthServer;
using AlexWoW.AuthServer.Net;
using AlexWoW.Database;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Serilog;

// Вспомогательный CLI: создание аккаунта без запуска сервера.
//   dotnet run -- create-account <username> <password>
if (args.Length >= 1 && args[0].Equals("create-account", StringComparison.OrdinalIgnoreCase))
{
    return await AccountCreator.RunAsync(args);
}

// CLI: массовая смена пароля — reset-all-passwords <password>
if (args.Length >= 1 && args[0].Equals("reset-all-passwords", StringComparison.OrdinalIgnoreCase))
{
    return await PasswordReset.RunAsync(args);
}

// CLI: флаг администратора — set-admin <username> [0|1]
if (args.Length >= 1 && args[0].Equals("set-admin", StringComparison.OrdinalIgnoreCase))
{
    return await AccountAdmin.RunAsync(args);
}

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Services.AddSerilog();

builder.Services.Configure<AuthServerOptions>(
    builder.Configuration.GetSection(AuthServerOptions.SectionName));
// Срез 3 рефактора DAL (#23): auth-путь на EF Core. Пул-фабрика контекста (singleton-safe, контекст
// на операцию) + EF-репозиторий вместо Dapper-AuthDatabase. Фиксированная ServerVersion — без коннекта
// при старте DI (готовность MySQL обеспечивает retry в AuthListener.EnsureSchemaWithRetryAsync).
builder.Services.AddPooledDbContextFactory<AuthDbContext>((sp, o) =>
{
    var options = sp.GetRequiredService<IOptions<AuthServerOptions>>().Value;
    o.UseMySql(options.ConnectionString, ServerVersion.Create(new Version(8, 4, 0), ServerType.MySql));
});
builder.Services.AddSingleton<IAccountRepository, EfAccountRepository>();
builder.Services.AddSingleton<IRealmRepository, EfRealmRepository>();
builder.Services.AddSingleton<ISchemaInitializer, AuthSchemaInitializer>();
builder.Services.AddHostedService<AuthListener>();

var host = builder.Build();

try
{
    Log.Information("AlexWoW AuthServer запускается…");
    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "AuthServer аварийно завершился");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
