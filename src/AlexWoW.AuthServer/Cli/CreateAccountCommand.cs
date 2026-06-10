using System.Security.Cryptography;
using AlexWoW.Cryptography;
using AlexWoW.Database.Abstractions;
using Microsoft.Extensions.Options;

namespace AlexWoW.AuthServer.Cli;

/// <summary>CLI-команда создания игрового аккаунта (генерирует SRP6 соль и верификатор).</summary>
internal sealed class CreateAccountCommand(
    IAccountRepository accounts,
    ISchemaInitializer schema,
    IOptions<AuthServerOptions> options) : ICliCommand
{
    public const string CommandName = "create-account";

    public string Name => CommandName;

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Использование: create-account <username> <password>");
            return 1;
        }

        var username = args[1];
        var password = args[2];

        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString))
        {
            Console.Error.WriteLine("Не задана строка подключения (AuthServer:ConnectionString).");
            return 1;
        }

        // Команда может выполняться на чистой БД до первого старта сервера — гарантируем схему.
        await schema.EnsureSchemaAsync(options.Value.DefaultRealm.ToRealm(), ct);

        if (await accounts.AccountExistsAsync(username, ct))
        {
            Console.Error.WriteLine($"Аккаунт '{username.ToUpperInvariant()}' уже существует.");
            return 1;
        }

        var salt = new byte[Srp6.SaltLength];
        RandomNumberGenerator.Fill(salt);
        var verifier = Srp6.ToFixedLittleEndian(
            Srp6.CalculateVerifier(username, password, salt), Srp6.KeyLength);

        await accounts.CreateAccountAsync(username, salt, verifier, ct: ct);
        Console.WriteLine($"Аккаунт '{username.ToUpperInvariant()}' создан.");
        return 0;
    }
}
