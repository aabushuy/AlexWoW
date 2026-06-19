// Порт CMaNGOS-WoTLK: src/game/AI/BaseAI/PetAI.cpp (PetAI::UpdateAI)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/AI/BaseAI/PetAI.cpp. GPL-2.0.

using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Pet;

/// <summary>
/// AI пета (PET.T2): минимальный тик-сервис. Полноценная интеграция с WorldCreature (физика, leash,
/// бой) — отдельная итерация (T2.1), сейчас только командно-реактивные изменения состояния.
/// </summary>
internal sealed class PetAIService(PetRegistry registry, ILogger<PetAIService> logger)
{
    /// <summary>Радиус leash к хозяину в ярдах (CMaNGOS PET_FOLLOW_DIST/leash).</summary>
    private const float LeashRadius = 50f;

    /// <summary>
    /// Тик AI всех активных петов — вызывается из WorldTick раз в N ms.
    /// </summary>
    public void Tick(WorldState world, long nowMs)
    {
        foreach (var pet in registry.All)
        {
            try { TickOne(world, pet, nowMs); }
            catch (Exception ex) { logger.LogDebug(ex, "PetAI tick failed pet={Id}: {Msg}", pet.Id, ex.Message); }
        }
    }

    private static void TickOne(WorldState world, World.Pet pet, long nowMs)
    {
        _ = nowMs;
        // Хозяин онлайн?
        var owner = world.FindPlayer(pet.OwnerGuid);
        if (owner is null)
            return;

        switch (pet.CommandState)
        {
            case PetCommandState.Stay:
                // ничего не делаем — пет стоит на месте
                break;
            case PetCommandState.Follow:
                // T2.1: позиция пета должна следовать за хозяином. Сейчас — pet считается «всегда рядом».
                break;
            case PetCommandState.Attack:
                // T2.1: пет атакует выбранную цель. Цель хранится отдельно (target guid).
                break;
        }

        // ReactState: T2.1 (после интеграции с боем).
        // Aggressive: искать врагов в радиусе → атаковать.
        // Defensive: атаковать, если хозяина бьют.
        // Passive: никогда не атаковать самостоятельно.
    }
}
