using AlexWoW.AuthServer;
using AlexWoW.AuthServer.Cli;
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

// Вспомогательный CLI (S10): команды выполняются на том же DI-хосте, что и сервер
// (общие репозитории, без дублирования сборки DAL): dotnet run -- <команда> …
// Имя определяем до сборки хоста, чтобы не отдавать позиционные аргументы CLI
// в command-line-провайдер конфигурации (как и прежний CLI, он их не использовал).
var cliCommandName = args.Length >= 1 ? args[0] : null;
var isCli = CreateAccountCommand.CommandName.Equals(cliCommandName, StringComparison.OrdinalIgnoreCase)
    || ResetAllPasswordsCommand.CommandName.Equals(cliCommandName, StringComparison.OrdinalIgnoreCase)
    || SetAdminCommand.CommandName.Equals(cliCommandName, StringComparison.OrdinalIgnoreCase);

var builder = Host.CreateApplicationBuilder(isCli ? [] : args);

if (isCli)
{
    // Прежний CLI читал appsettings.json из каталога бинарника (а не из cwd, как хост) —
    // сохраняем источник, иначе `dotnet run` из корня репозитория не найдёт конфиг.
    // Env-переменные добавляются повторно, чтобы остаться приоритетнее json (как раньше).
    builder.Configuration
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
        .AddEnvironmentVariables();
}

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

// CLI-команды (S10): создание аккаунта, массовый сброс паролей, флаг администратора.
builder.Services.AddSingleton<ICliCommand, CreateAccountCommand>();
builder.Services.AddSingleton<ICliCommand, ResetAllPasswordsCommand>();
builder.Services.AddSingleton<ICliCommand, SetAdminCommand>();

var host = builder.Build();

if (isCli)
{
    // Hosted-сервисы не стартуют: host.RunAsync не вызывается, выполняем команду и выходим.
    var command = host.Services.GetServices<ICliCommand>()
        .First(c => c.Name.Equals(cliCommandName, StringComparison.OrdinalIgnoreCase));
    return await command.RunAsync(args, CancellationToken.None);
}

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
