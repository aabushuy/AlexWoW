using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Репозиторий инвентаря персонажа (таблица <c>character_items</c>, БД <c>alexwow_auth</c>).
/// Часть DAL-фасада <see cref="ICharacterStore"/>. Срез 1 рефактора DAL (#23).
/// </summary>
public interface IInventoryRepository
{
    /// <summary>Есть ли у персонажа хоть один предмет (для выдачи стартового набора).</summary>
    Task<bool> HasItemsAsync(uint ownerGuid, CancellationToken ct = default);

    /// <summary>Инвентарь персонажа (все предметы во всех слотах).</summary>
    Task<IReadOnlyList<InventoryItem>> GetItemsAsync(uint ownerGuid, CancellationToken ct = default);

    /// <summary>Кладёт предмет в слот. Возвращает low-counter GUID нового предмета.</summary>
    Task<uint> AddItemAsync(uint ownerGuid, uint itemEntry, byte bag, byte slot,
        uint stackCount = 1, CancellationToken ct = default);

    /// <summary>Удаляет предмет персонажа по его low-counter GUID.</summary>
    Task RemoveItemAsync(uint itemGuid, CancellationToken ct = default);

    /// <summary>Перемещает предмет в другой контейнер/слот.</summary>
    Task MoveItemAsync(uint itemGuid, byte bag, byte slot, CancellationToken ct = default);

    /// <summary>Меняет размер стопки предмета.</summary>
    Task SetItemStackAsync(uint itemGuid, uint stackCount, CancellationToken ct = default);
}
