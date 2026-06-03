using System.Net;
using System.Net.Sockets;
using AlexWoW.Database;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlexWoW.WorldServer.Net;

/// <summary>Принимает world-соединения и запускает <see cref="WorldSession"/> на каждое.</summary>
public sealed class WorldListener(
    IOptions<WorldServerOptions> options,
    AuthDatabase database,
    CharactersDatabase characters,
    WorldState world,
    ILogger<WorldListener> logger) : BackgroundService
{
    private readonly WorldServerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureSchemaWithRetryAsync(stoppingToken);

        var endpoint = new IPEndPoint(IPAddress.Parse(_options.BindAddress), _options.Port);
        using var listener = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(endpoint);
        listener.Listen(128);
        logger.LogInformation("WorldServer слушает {Endpoint} ({Handlers} опкодов зарегистрировано)",
            endpoint, Handlers.WorldPacketRouter.HandlerCount);

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

            _ = Task.Run(
                () => new WorldSession(client, database, characters, world, _options, logger).RunAsync(stoppingToken),
                stoppingToken);
        }
    }

    private async Task EnsureSchemaWithRetryAsync(CancellationToken ct)
    {
        const int maxAttempts = 30;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await characters.EnsureSchemaAsync(ct);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(5, attempt));
                logger.LogWarning("БД персонажей недоступна (попытка {Attempt}/{Max}): {Message}. Повтор через {Delay}s",
                    attempt, maxAttempts, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }
    }
}
