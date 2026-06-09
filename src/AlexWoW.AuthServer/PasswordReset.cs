using System.Security.Cryptography;
using AlexWoW.Cryptography;
using Microsoft.Extensions.Configuration;

namespace AlexWoW.AuthServer;

/// <summary>
/// CLI-команда массовой смены пароля: <c>reset-all-passwords &lt;password&gt;</c> — ставит один пароль
/// всем аккаунтам (новые SRP6 соль+верификатор, session_key сбрасывается). Тестовый стенд.
/// </summary>
public static class PasswordReset
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Использование: reset-all-passwords <password>");
            return 1;
        }
        var password = args[1];

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
        var users = await database.GetAllUsernamesAsync();
        foreach (var username in users)
        {
            var salt = new byte[Srp6.SaltLength];
            RandomNumberGenerator.Fill(salt);
            var verifier = Srp6.ToFixedLittleEndian(
                Srp6.CalculateVerifier(username, password, salt), Srp6.KeyLength);
            await database.UpdatePasswordAsync(username, salt, verifier);
        }

        Console.WriteLine($"Пароль изменён на '{password}' у {users.Count} аккаунтов.");
        return 0;
    }
}
