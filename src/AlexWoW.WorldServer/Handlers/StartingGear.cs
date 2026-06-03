using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Выдача стартовой экипировки (M6.1). Источник — playercreateinfo_item (наполнен офлайн из
/// CharStartOutfit.dbc, см. tools/MapExtractor). Экипируемое раскладываем по слотам экипировки
/// (0..18) по InventoryType, прочее — в рюкзак (23..38). Слот сохраняется в character_items.
/// </summary>
public static class StartingGear
{
    /// <summary>Выдаёт стартовый набор персонажу (если БД мира доступна). Идемпотентность — на вызывающем.</summary>
    public static async Task GiveAsync(WorldSession session, uint charGuid, byte race, byte cls, CancellationToken ct)
    {
        IReadOnlyList<Database.Models.StartingItem> starting;
        try
        {
            starting = await session.WorldDb.GetStartingItemsAsync(race, cls, ct);
        }
        catch (Exception ex)
        {
            session.Logger.LogWarning("Стартовый набор недоступен (БД мира): {Msg}", ex.Message);
            return;
        }

        var equipUsed = new bool[InventorySlots.EquipmentEnd]; // слоты 0..18
        var backpack = InventorySlots.BackpackStart;
        var given = 0;

        foreach (var item in starting)
        {
            int slot;
            var equip = InventorySlots.EquipSlotFor(item.InventoryType);
            if (equip >= 0 && !equipUsed[equip])
            {
                slot = equip;
                equipUsed[equip] = true;
            }
            else if (backpack < InventorySlots.BackpackEnd)
            {
                slot = backpack++;
            }
            else
            {
                continue; // рюкзак переполнен — пропускаем
            }

            var count = (uint)Math.Max(1, (int)item.Amount);
            await session.Characters.AddItemAsync(charGuid, item.ItemId, InventorySlots.MainBag, (byte)slot, count, ct);
            given++;
        }

        session.Logger.LogInformation("Стартовый набор: выдано {Count} предметов (раса {Race}, класс {Class})",
            given, race, cls);
    }
}
