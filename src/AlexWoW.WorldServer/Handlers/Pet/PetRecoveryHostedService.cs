// Порт CMaNGOS-WoTLK: src/game/Entities/Pet.cpp (LoadPetFromDB lazy on Player::LoadFromDB)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Entities/Pet.cpp. GPL-2.0.

using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Pet;

/// <summary>При старте сервера загружает всех петов в PetRegistry. PET.T5.</summary>
internal sealed class PetRecoveryHostedService(
    IPetRepository repo,
    PetRegistry registry,
    ILogger<PetRecoveryHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var pets = await repo.LoadAllAsync(ct);
            foreach (var p in pets)
            {
                registry.Rehydrate(p.Id, p.OwnerGuid, p.Entry, p.Name, p.Level,
                    (PetType)p.Type, (PetReactState)p.ReactState, (PetCommandState)p.CommandState,
                    p.Happiness, p.Experience);
            }
            logger.LogInformation("PET recovery: загружено {N} питомцев", pets.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PET recovery failed: {Msg}", ex.Message);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
