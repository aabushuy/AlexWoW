using AlexWoW.Database.Models;

namespace AlexWoW.WorldServer.Net.SessionState;

/// <summary>
/// Инвентарь сессии: предметы, кэш сумок, деньги, окно лута.
/// Выделено из плоских полей <see cref="WorldSession"/>. M7 S9 #43.
/// </summary>
internal sealed class SessionInventoryState
{
    /// <summary>Инвентарь персонажа в мире (предметы во всех слотах). Загружается при входе. M6.1.</summary>
    internal List<InventoryItem> Inventory { get; } = new();

    /// <summary>Кэш class/ContainerSlots/MaxDurability по entry предметов инвентаря (батч при входе) —
    /// чтобы знать, какие предметы суммки (контейнеры), без запроса БД на каждый предмет. M6.13.</summary>
    internal Dictionary<uint, ItemBagInfo> ItemBagInfo { get; } = new();

    /// <summary>Деньги персонажа (медь) в мире. Загружается при входе, меняется торговлей. M6.2.</summary>
    internal uint Money { get; set; }

    /// <summary>GUID трупа с открытым окном лута (0 — окно закрыто). M6.6.</summary>
    internal ulong LootGuid { get; set; }

    /// <summary>Сброс при выходе из мира — только то, что сбрасывалось в LeaveWorld и раньше
    /// (Money переживает выход by design — перезагружается при входе).</summary>
    internal void Reset()
    {
        LootGuid = 0;        // M6.6: окно лута закрыто
        Inventory.Clear();
        ItemBagInfo.Clear(); // M6.13: кэш сумок перезагружается при следующем входе
    }
}
