using System.Net;
using System.Net.Sockets;
using AlexWoW.Database;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlexWoW.WorldServer.Net;

/// <summary>Принимает world-соединения и запускает <see cref="WorldSession"/> на каждое.</summary>
public sealed class WorldListener(
    IOptions<WorldServerOptions> options,
    AuthDatabase database,
    ILogger<WorldListener> logger) : BackgroundService
{
    private readonly WorldServerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(_options.BindAddress), _options.Port);
        using var listener = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(endpoint);
        listener.Listen(128);
        logger.LogInformation("WorldServer слушает {Endpoint}", endpoint);

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
                () => new WorldSession(client, database, _options, logger).RunAsync(stoppingToken),
                stoppingToken);
        }
    }
}
