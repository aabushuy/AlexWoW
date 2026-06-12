using AlexWoW.Database.Models;

namespace AlexWoW.Web.Services;

/// <summary>Экипированный предмет (слот 0..18 основного контейнера) для карточки персонажа.</summary>
public sealed record EquipItem(byte Slot, string SlotName, uint Entry, uint Stack);

/// <summary>
/// Модель общей карточки персонажа (partial <c>_CharacterCard</c>): атрибуты + экипировка.
/// Переиспользуется клиентской страницей «Персонаж» и админ-правкой (M8.9 / задача #111).
/// </summary>
public sealed class CharacterCardModel
{
    public required Character Character { get; init; }
    public IReadOnlyList<EquipItem> Equipment { get; init; } = [];

    /// <summary>INVENTORY_SLOT_BAG_0 — основной контейнер, где лежит экипировка (слоты 0..18).</summary>
    public const byte MainBag = 255;

    /// <summary>Собирает список экипировки из инвентаря (только надетые слоты основного контейнера).</summary>
    public static IReadOnlyList<EquipItem> BuildEquipment(IEnumerable<InventoryItem> items) =>
        [.. items
            .Where(i => i.Bag == MainBag && GameData.EquipSlotName(i.Slot) is not null)
            .OrderBy(i => i.Slot)
            .Select(i => new EquipItem(i.Slot, GameData.EquipSlotName(i.Slot)!, i.ItemEntry, i.StackCount))];
}
