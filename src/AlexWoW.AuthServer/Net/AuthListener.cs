using System.Net;
using System.Net.Sockets;
using AlexWoW.Database;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlexWoW.AuthServer.Net;

/// <summary>Принимает TCP-соединения логин-протокола и запускает <see cref="AuthSession"/> на каждое.</summary>
public sealed class AuthListener(
    IOptions<AuthServerOptions> options,
    AuthDatabase database,
    ILogger<AuthListener> logger) : BackgroundService
{
    private readonly AuthServerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await database.EnsureSchemaAsync(ToRealm(_options.DefaultRealm), stoppingToken);

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
            _ = Task.Run(() => new AuthSession(client, database, logger).RunAsync(stoppingToken), stoppingToken);
        }
    }

    private static Database.Models.Realm ToRealm(DefaultRealmOptions o) => new()
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
