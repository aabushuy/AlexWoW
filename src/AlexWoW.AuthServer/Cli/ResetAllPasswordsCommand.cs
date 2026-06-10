using System.Security.Cryptography;
using AlexWoW.Cryptography;
using AlexWoW.Database.Abstractions;
using Microsoft.Extensions.Options;

namespace AlexWoW.AuthServer.Cli;

/// <summary>
/// CLI-команда массовой смены пароля: <c>reset-all-passwords &lt;password&gt;</c> — ставит один пароль
/// всем аккаунтам (новые SRP6 соль+верификатор, session_key сбрасывается). Тестовый стенд.
/// </summary>
internal sealed class ResetAllPasswordsCommand(
    IAccountRepository accounts,
    IOptions<AuthServerOptions> options) : ICliCommand
{
    public const string CommandName = "reset-all-passwords";

    public string Name => CommandName;

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Использование: reset-all-passwords <password>");
            return 1;
        }
        var password = args[1];

        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString))
        {
            Console.Error.WriteLine("Не задана строка подключения (AuthServer:ConnectionString).");
            return 1;
        }

        var users = await accounts.GetAllUsernamesAsync(ct);
        foreach (var username in users)
        {
            var salt = new byte[Srp6.SaltLength];
            RandomNumberGenerator.Fill(salt);
            var verifier = Srp6.ToFixedLittleEndian(
                Srp6.CalculateVerifier(username, password, salt), Srp6.KeyLength);
            await accounts.UpdatePasswordAsync(username, salt, verifier, ct);
        }

        Console.WriteLine($"Пароль изменён на '{password}' у {users.Count} аккаунтов.");
        return 0;
    }
}
