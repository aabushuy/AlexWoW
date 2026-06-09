using Microsoft.Extensions.Configuration;

namespace AlexWoW.AuthServer;

/// <summary>
/// CLI: выдать/снять флаг администратора аккаунту (доступ к DevCommands в мире).
/// <c>set-admin &lt;username&gt; [0|1]</c> — без 3-го аргумента ставит 1. M7.
/// </summary>
public static class AccountAdmin
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Использование: set-admin <username> [0|1]");
            return 1;
        }
        var username = args[1];
        var isAdmin = args.Length < 3 || args[2] != "0";

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new AuthServerOptions();
        config.GetSection(AuthServerOptions.SectionName).Bind(options);
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Console.Error.WriteLine("Не задана строка подключения (AuthServer:ConnectionString).");
            return 1;
        }

        var database = CliRepository.CreateAccountRepository(options.ConnectionString);
        var affected = await database.SetAdminAsync(username, isAdmin);
        if (affected == 0)
        {
            Console.Error.WriteLine($"Аккаунт '{username}' не найден.");
            return 1;
        }
        Console.WriteLine($"Аккаунт '{username}': is_admin = {(isAdmin ? 1 : 0)}.");
        return 0;
    }
}
