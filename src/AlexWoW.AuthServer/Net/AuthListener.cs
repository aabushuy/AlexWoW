using System.Net;
using System.Net.Sockets;
using AlexWoW.Database.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlexWoW.AuthServer.Net;

/// <summary>Принимает TCP-соединения логин-протокола и запускает <see cref="AuthSession"/> на каждое.</summary>
public sealed class AuthListener(
    IOptions<AuthServerOptions> options,
    IAccountRepository account,
    IRealmRepository realms,
    ISchemaInitializer schema,
    ILogger<AuthListener> logger) : BackgroundService
{
    private readonly AuthServerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureSchemaWithRetryAsync(stoppingToken);

        var endpoint = new IPEndPoint(IPAddress.Parse(_options.BindAddress), _options.Port);
        using var listener = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(endpoint);
        listener.Listen(128);
        logger.LogInformation("AuthServer слушает {Endpoint}", endpoint);

        while (!stoppingToken.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Каждую сессию обрабатываем независимо, не блокируя accept-цикл.
            _ = Task.Run(() => new AuthSession(client, account, realms, logger).RunAsync(stoppingToken), stoppingToken);
        }
    }

    /// <summary>
    /// Ждёт готовности MySQL и создаёт схему. БД может стартовать чуть дольше контейнера,
    /// поэтому повторяем с экспоненциальной задержкой вместо падения сервиса.
    /// </summary>
    private async Task EnsureSchemaWithRetryAsync(CancellationToken ct)
    {
        const int maxAttempts = 30;
        var realm = _options.DefaultRealm.ToRealm();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await schema.EnsureSchemaAsync(realm, ct);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(5, attempt));
                logger.LogWarning("БД недоступна (попытка {Attempt}/{Max}): {Message}. Повтор через {Delay}s",
                    attempt, maxAttempts, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }
    }
}
