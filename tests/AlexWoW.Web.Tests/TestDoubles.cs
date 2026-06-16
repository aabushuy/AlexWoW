using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;

namespace AlexWoW.Web.Tests;

/// <summary>Фейк репозитория персонажей: знает одного персонажа и фиксирует записи расы/пола/денег.</summary>
internal sealed class FakeCharacterRepository(Character character) : ICharacterRepository
{
    public byte? SetRaceGenderRace { get; private set; }
    public byte? SetRaceGenderGender { get; private set; }
    public uint? SetMoneyValue { get; private set; }

    public Task<Character?> GetByGuidAsync(uint guid, CancellationToken ct = default) =>
        Task.FromResult<Character?>(guid == character.Guid ? character : null);

    public Task<IReadOnlyList<Character>> GetByAccountAsync(uint accountId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Character>>(accountId == character.AccountId ? [character] : []);

    public Task SetRaceGenderAsync(uint guid, byte race, byte gender, CancellationToken ct = default)
    {
        SetRaceGenderRace = race;
        SetRaceGenderGender = gender;
        return Task.CompletedTask;
    }

    public Task SetMoneyAsync(uint guid, uint money, CancellationToken ct = default)
    {
        SetMoneyValue = money;
        return Task.CompletedTask;
    }

    public Task EnsureSchemaAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> CountByAccountAsync(uint accountId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<uint> CreateAsync(Character character, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SavePositionAsync(uint guid, float x, float y, float z, uint map, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SetDeclinedNamesAsync(uint ownerGuid, string[] names, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<HashSet<uint>> GetGuidsWithDeclinedNamesAsync(IReadOnlyCollection<uint> guids, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<string[]?> GetDeclinedNamesAsync(uint ownerGuid, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SetTalentResetCostAsync(uint guid, uint cost, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SetLevelXpAsync(uint guid, byte level, uint xp, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SetActionBarsAsync(uint guid, byte actionBars, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SetTesterAsync(uint guid, bool isTester, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<Character>> GetTestersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Character>>([]);
    public Task<bool> DeleteAsync(uint guid, uint accountId, CancellationToken ct = default) => throw new NotImplementedException();
}

/// <summary>Фейк инвентаря: персонаж без предметов.</summary>
internal sealed class EmptyInventory : IInventoryRepository
{
    public Task<IReadOnlyList<InventoryItem>> GetItemsAsync(uint ownerGuid, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<InventoryItem>>([]);
    public Task<bool> HasItemsAsync(uint ownerGuid, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<uint> AddItemAsync(uint ownerGuid, uint itemEntry, byte bag, byte slot, uint stackCount = 1, CancellationToken ct = default) => throw new NotImplementedException();
    public Task RemoveItemAsync(uint itemGuid, CancellationToken ct = default) => throw new NotImplementedException();
    public Task MoveItemAsync(uint itemGuid, byte bag, byte slot, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SetItemStackAsync(uint itemGuid, uint stackCount, CancellationToken ct = default) => throw new NotImplementedException();
}

/// <summary>Фейк поиска предметов: возвращает заранее заданный набор, игнорируя фильтр.</summary>
internal sealed class FakeItemSearchRepository(IReadOnlyList<ItemTemplateData> items) : IItemSearchRepository
{
    public ItemSearchFilter? LastFilter { get; private set; }

    public Task<IReadOnlyList<ItemTemplateData>> SearchAsync(ItemSearchFilter filter, CancellationToken ct = default)
    {
        LastFilter = filter;
        return Task.FromResult(items);
    }
}

/// <summary>Фейк настроек: ключей нет → используются дефолты (1000/2000).</summary>
internal sealed class EmptySettings : ISettingRepository
{
    public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}
