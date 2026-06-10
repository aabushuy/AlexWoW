using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages.Characters;

/// <summary>Карточка персонажа: атрибуты + экипировка (только свои персонажи).</summary>
public sealed class DetailsModel(ICharacterRepository characters, IInventoryRepository inventory) : PageModel
{
    /// <summary>Экипированный предмет (слот 0..18 основного контейнера).</summary>
    public sealed record EquipItem(byte Slot, string SlotName, uint Entry, uint Stack);

    public Character Character { get; private set; } = null!;
    public IReadOnlyList<EquipItem> Equipment { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(uint guid, CancellationToken ct)
    {
        var character = await characters.GetByGuidAsync(guid, ct);
        // Доступ только к своим персонажам — иначе чужой аккаунт мог бы листать по guid.
        if (character is null || character.AccountId != User.AccountId())
            return NotFound();

        Character = character;

        var items = await inventory.GetItemsAsync(guid, ct);
        Equipment = [.. items
            .Where(i => i.Bag == InventorySlots.MainBag && GameData.EquipSlotName(i.Slot) is not null)
            .OrderBy(i => i.Slot)
            .Select(i => new EquipItem(i.Slot, GameData.EquipSlotName(i.Slot)!, i.ItemEntry, i.StackCount))];

        return Page();
    }

    /// <summary>Слоты экипировки 0..18 (INVENTORY_SLOT_BAG_0).</summary>
    private static class InventorySlots
    {
        public const byte MainBag = 255;
    }
}
