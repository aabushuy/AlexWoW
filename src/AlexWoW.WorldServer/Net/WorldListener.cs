using System.Net;
using System.Net.Sockets;
using AlexWoW.Database.Abstractions;
using AlexWoW.DataStores.Collision;
using AlexWoW.DataStores.Navigation;
using AlexWoW.DataStores.Terrain;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlexWoW.WorldServer.Net;

/// <summary>Принимает world-соединения и запускает <see cref="WorldSession"/> на каждое.</summary>
public sealed class WorldListener(
    IOptions<WorldServerOptions> options,
    IAccountRepository database,
    ICharacterStore characters,
    IWorldRepository worldDatabase,
    TerrainMaps terrain,
    Vmaps vmaps,
    Navmesh navmesh,
    WorldState world,
    ILogger<WorldListener> logger) : BackgroundService
{
    private readonly WorldServerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureSchemaWithRetryAsync(stoppingToken);
        await ProbeWorldDatabaseAsync(stoppingToken);
        logger.LogInformation(terrain.Available
            ? "Рельеф (maps) подключён"
            : "Рельеф (maps) не задан — высота земли недоступна");
        logger.LogInformation("Коллизии (vmaps): {V}; навмеш (mmaps): {M}",
            vmaps.Available ? "подключены" : "нет", navmesh.Available ? "подключён" : "нет");

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

            // Отключаем алгоритм Нейгла: протокол шлёт много мелких пакетов (движение),
            // их батчинг даёт рывки/«полёт» у соседних игроков. Нужна минимальная задержка.
            client.NoDelay = true;

            _ = Task.Run(
                () => new WorldSession(client, database, characters, worldDatabase, terrain, world, _options, logger)
                    .RunAsync(stoppingToken),
                stoppingToken);
        }
    }

    /// <summary>Лог о доступности БД мира (не фатально, если её нет — сработает fallback на тест-NPC).</summary>
    private async Task ProbeWorldDatabaseAsync(CancellationToken ct)
    {
        try
        {
            var count = await worldDatabase.CountCreaturesAsync(ct);
            logger.LogInformation("БД мира подключена: {Count} спавнов существ", count);
        }
        catch (Exception ex)
        {
            logger.LogWarning("БД мира недоступна ({Msg}) — NPC из дампа не будут спавниться", ex.Message);
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
