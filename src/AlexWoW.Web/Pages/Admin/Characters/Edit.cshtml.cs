using System.ComponentModel.DataAnnotations;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages.Admin.Characters;

/// <summary>
/// Админ-правка персонажа (M8.9): смена расы, пола и количества золота. Раса — любая (админ-инструмент),
/// внешность не трогаем. После сохранения возвращаемся в карточку аккаунта-владельца.
/// </summary>
public sealed class EditModel(ICharacterRepository characters, IInventoryRepository inventory) : PageModel
{
    public CharacterCardModel Card { get; private set; } = null!;
    public Character Character => Card.Character;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        public byte Race { get; set; }
        public byte Gender { get; set; }

        [Range(0, 429496, ErrorMessage = "Золото: 0–429496.")]
        public uint Gold { get; set; }

        [Range(0, 99, ErrorMessage = "Серебро: 0–99.")]
        public uint Silver { get; set; }

        [Range(0, 99, ErrorMessage = "Медь: 0–99.")]
        public uint Copper { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(uint guid, CancellationToken ct)
    {
        if (!await LoadAsync(guid, ct))
            return NotFound();

        var (gold, silver, copper) = GameData.SplitMoney(Character.Money);
        Input = new InputModel
        {
            Race = Character.Race,
            Gender = Character.Gender,
            Gold = gold,
            Silver = silver,
            Copper = copper,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(uint guid, CancellationToken ct)
    {
        if (!await LoadAsync(guid, ct))
            return NotFound();

        if (!GameData.RaceExists(Input.Race))
            ModelState.AddModelError("Input.Race", "Неизвестная раса.");
        if (Input.Gender > 1)
            ModelState.AddModelError("Input.Gender", "Неизвестный пол.");
        if (!ModelState.IsValid)
            return Page();

        await characters.SetRaceGenderAsync(guid, Input.Race, Input.Gender, ct);
        await characters.SetMoneyAsync(guid, ToCopper(Input.Gold, Input.Silver, Input.Copper), ct);

        TempData["Flash"] = $"Персонаж «{Character.Name}» обновлён.";
        return RedirectToPage("/Admin/Accounts/Edit", new { id = Character.AccountId });
    }

    /// <summary>Сводит золото/серебро/медь в медь с защитой от переполнения uint.</summary>
    private static uint ToCopper(uint gold, uint silver, uint copper)
    {
        var total = (ulong)gold * 10000 + silver * 100 + copper;
        return total > uint.MaxValue ? uint.MaxValue : (uint)total;
    }

    private async Task<bool> LoadAsync(uint guid, CancellationToken ct)
    {
        var character = await characters.GetByGuidAsync(guid, ct);
        if (character is null)
            return false;
        var items = await inventory.GetItemsAsync(guid, ct);
        Card = new CharacterCardModel { Character = character, Equipment = CharacterCardModel.BuildEquipment(items) };
        return true;
    }
}
