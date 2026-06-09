using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;

namespace AlexWoW.Database.Repositories.World;

/// <summary>
/// Композитный фасад read-only БД мира (<see cref="IWorldRepository"/>) — делегирует focused-репозиториям.
/// Рефактор #25 (SOLID): единая точка доступа для <c>WorldSession.WorldDb</c> без логики/SQL (вся логика —
/// в SRP-репозиториях). Точечные потребители (<c>*Store</c>) должны зависеть от УЗКИХ интерфейсов напрямую.
/// </summary>
public sealed class WorldRepository(
    ICreatureRepository creatures,
    IGameObjectRepository gameObjects,
    IItemTemplateRepository items,
    IVendorRepository vendors,
    ITrainerRepository trainers,
    ILootRepository loot,
    IQuestTemplateRepository quests,
    IFactionRepository factions,
    IPlayerDataRepository playerData) : IWorldRepository
{
    // ---- ICreatureRepository ----
    public Task<long> CountCreaturesAsync(CancellationToken ct = default)
        => creatures.CountCreaturesAsync(ct);
    public Task<IReadOnlyList<CreatureSpawnData>> GetCreaturesNearAsync(
        uint map, float x, float y, float range, int limit, CancellationToken ct = default)
        => creatures.GetCreaturesNearAsync(map, x, y, range, limit, ct);
    public Task<CreatureTemplateData?> GetCreatureTemplateAsync(uint entry, CancellationToken ct = default)
        => creatures.GetCreatureTemplateAsync(entry, ct);

    // ---- IGameObjectRepository ----
    public Task<IReadOnlyList<GameObjectSpawnData>> GetGameObjectsNearAsync(
        uint map, float x, float y, float range, int limit, CancellationToken ct = default)
        => gameObjects.GetGameObjectsNearAsync(map, x, y, range, limit, ct);
    public Task<GameObjectTemplateData?> GetGameObjectTemplateAsync(uint entry, CancellationToken ct = default)
        => gameObjects.GetGameObjectTemplateAsync(entry, ct);

    // ---- IItemTemplateRepository ----
    public Task<ItemTemplateData?> GetItemTemplateAsync(uint entry, CancellationToken ct = default)
        => items.GetItemTemplateAsync(entry, ct);
    public Task<IReadOnlyDictionary<uint, (uint DisplayId, byte InventoryType)>> GetItemDisplaysAsync(
        IReadOnlyCollection<uint> entries, CancellationToken ct = default)
        => items.GetItemDisplaysAsync(entries, ct);

    // ---- IVendorRepository ----
    public Task<IReadOnlyList<VendorItem>> GetVendorItemsAsync(uint entry, CancellationToken ct = default)
        => vendors.GetVendorItemsAsync(entry, ct);

    // ---- ITrainerRepository ----
    public Task<TrainerData?> GetTrainerAsync(uint entry, CancellationToken ct = default)
        => trainers.GetTrainerAsync(entry, ct);

    // ---- ILootRepository ----
    public Task<CreatureLootData?> GetCreatureLootAsync(uint creatureEntry, CancellationToken ct = default)
        => loot.GetCreatureLootAsync(creatureEntry, ct);

    // ---- IQuestTemplateRepository ----
    public Task<IReadOnlyList<uint>> GetQuestGiverEntriesAsync(CancellationToken ct = default)
        => quests.GetQuestGiverEntriesAsync(ct);
    public Task<IReadOnlyList<uint>> GetQuestEnderEntriesAsync(CancellationToken ct = default)
        => quests.GetQuestEnderEntriesAsync(ct);
    public Task<IReadOnlyList<QuestRelation>> GetQuestGiverRelationsAsync(CancellationToken ct = default)
        => quests.GetQuestGiverRelationsAsync(ct);
    public Task<IReadOnlyList<QuestRelation>> GetQuestEnderRelationsAsync(CancellationToken ct = default)
        => quests.GetQuestEnderRelationsAsync(ct);
    public Task<IReadOnlyList<GiverQuest>> GetGiverQuestsAsync(uint creatureEntry, CancellationToken ct = default)
        => quests.GetGiverQuestsAsync(creatureEntry, ct);
    public Task<IReadOnlyList<uint>> GetEnderQuestIdsAsync(uint creatureEntry, CancellationToken ct = default)
        => quests.GetEnderQuestIdsAsync(creatureEntry, ct);
    public Task<QuestTemplateData?> GetQuestAsync(uint entry, CancellationToken ct = default)
        => quests.GetQuestAsync(entry, ct);

    // ---- IFactionRepository ----
    public Task<IReadOnlyList<FactionTemplateRow>> GetFactionTemplatesAsync(CancellationToken ct = default)
        => factions.GetFactionTemplatesAsync(ct);

    // ---- IPlayerDataRepository ----
    public Task<IReadOnlyList<StartingItem>> GetStartingItemsAsync(byte race, byte cls, CancellationToken ct = default)
        => playerData.GetStartingItemsAsync(race, cls, ct);
    public Task<IReadOnlyList<uint>> GetStartSpellsAsync(byte race, byte cls, CancellationToken ct = default)
        => playerData.GetStartSpellsAsync(race, cls, ct);
    public Task<IReadOnlyDictionary<(byte Class, byte Level), (uint Hp, uint Mana)>> GetClassLevelStatsAsync(CancellationToken ct = default)
        => playerData.GetClassLevelStatsAsync(ct);
    public Task<IReadOnlyDictionary<(byte Race, byte Class, byte Level), (uint Str, uint Agi, uint Sta, uint Int, uint Spi)>> GetLevelStatsAsync(CancellationToken ct = default)
        => playerData.GetLevelStatsAsync(ct);
    public Task<IReadOnlyDictionary<uint, uint>> GetXpForLevelTableAsync(CancellationToken ct = default)
        => playerData.GetXpForLevelTableAsync(ct);
}
