using AlexWoW.Database.Abstractions;
using Microsoft.Extensions.Options;

namespace AlexWoW.AuthServer.Cli;

/// <summary>
/// CLI: выдать/снять флаг администратора аккаунту (доступ к DevCommands в мире).
/// <c>set-admin &lt;username&gt; [0|1]</c> — без 3-го аргумента ставит 1. M7.
/// </summary>
internal sealed class SetAdminCommand(
    IAccountRepository accounts,
    IOptions<AuthServerOptions> options) : ICliCommand
{
    public const string CommandName = "set-admin";

    public string Name => CommandName;

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Использование: set-admin <username> [0|1]");
            return 1;
        }
        var username = args[1];
        var isAdmin = args.Length < 3 || args[2] != "0";

        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString))
        {
            Console.Error.WriteLine("Не задана строка подключения (AuthServer:ConnectionString).");
            return 1;
        }

        var affected = await accounts.SetAdminAsync(username, isAdmin, ct);
        if (affected == 0)
        {
            Console.Error.WriteLine($"Аккаунт '{username}' не найден.");
            return 1;
        }
        Console.WriteLine($"Аккаунт '{username}': is_admin = {(isAdmin ? 1 : 0)}.");
        return 0;
    }
}
