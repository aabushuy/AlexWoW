using System.Security.Cryptography;
using AlexWoW.Cryptography;
using AlexWoW.Database.Models;
using Microsoft.Extensions.Configuration;

namespace AlexWoW.AuthServer;

/// <summary>CLI-команда создания игрового аккаунта (генерирует SRP6 соль и верификатор).</summary>
public static class AccountCreator
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Использование: create-account <username> <password>");
            return 1;
        }

        var username = args[1];
        var password = args[2];

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

        await CliRepository.CreateSchemaInitializer(options.ConnectionString)
            .EnsureSchemaAsync(ToRealm(options.DefaultRealm));

        var database = CliRepository.CreateAccountRepository(options.ConnectionString);
        if (await database.AccountExistsAsync(username))
        {
            Console.Error.WriteLine($"Аккаунт '{username.ToUpperInvariant()}' уже существует.");
            return 1;
        }

        var salt = new byte[Srp6.SaltLength];
        RandomNumberGenerator.Fill(salt);
        var verifier = Srp6.ToFixedLittleEndian(
            Srp6.CalculateVerifier(username, password, salt), Srp6.KeyLength);

        await database.CreateAccountAsync(username, salt, verifier);
        Console.WriteLine($"Аккаунт '{username.ToUpperInvariant()}' создан.");
        return 0;
    }

    private static Realm ToRealm(DefaultRealmOptions o) => new()
    {
        Name = o.Name,
        Address = o.Address,
        Port = o.Port,
        Type = o.Type,
        Flags = o.Flags,
        Timezone = o.Timezone,
        Population = o.Population,
    };
}
