using System.ComponentModel.DataAnnotations;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages.Characters;

/// <summary>
/// Карточка персонажа (только свои): атрибуты + экипировка (общий компонент _CharacterCard),
/// смена расы/пола (M8.6) и покупка игрового золота (M8.7).
/// </summary>
public sealed class DetailsModel(ICharacterRepository characters, IInventoryRepository inventory) : PageModel
{
    /// <summary>Максимум золота за одну покупку (M8.7, демо-режим без реальной оплаты).</summary>
    public const uint MaxGoldPerPurchase = 100_000;

    public CharacterCardModel Card { get; private set; } = null!;
    public Character Character => Card.Character;

    /// <summary>Доступные расы для смены (той же фракции, валидные для класса) — M8.6.</summary>
    public IReadOnlyList<KeyValuePair<byte, string>> AvailableRaces { get; private set; } = [];

    [TempData]
    public string? Flash { get; set; }

    [BindProperty]
    public AppearanceInput Appearance { get; set; } = new();

    [BindProperty]
    public GoldInput Gold { get; set; } = new();

    public sealed class AppearanceInput
    {
        public byte Race { get; set; }
        public byte Gender { get; set; }
    }

    public sealed class GoldInput
    {
        [Range(1, MaxGoldPerPurchase, ErrorMessage = "Введите от 1 до 100000 золота.")]
        public uint Amount { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(uint guid, CancellationToken ct)
    {
        if (!await LoadOwnAsync(guid, ct))
            return NotFound();
        Appearance = new AppearanceInput { Race = Character.Race, Gender = Character.Gender };
        return Page();
    }

    public async Task<IActionResult> OnPostAppearanceAsync(uint guid, CancellationToken ct)
    {
        if (!await LoadOwnAsync(guid, ct))
            return NotFound();

        // Раса — только из доступного набора (та же фракция + валидна для класса); пол — 0/1.
        if (!AvailableRaces.Any(r => r.Key == Appearance.Race))
            ModelState.AddModelError("Appearance.Race", "Эта раса недоступна для вашего класса/фракции.");
        if (Appearance.Gender > 1)
            ModelState.AddModelError("Appearance.Gender", "Неизвестный пол.");
        if (!ModelState.IsValid)
            return Page();

        await characters.SetRaceGenderAsync(guid, Appearance.Race, Appearance.Gender, ct);
        Flash = "Раса/пол изменены. Изменения вступят в силу при следующем входе в игру.";
        return RedirectToPage(new { guid });
    }

    public async Task<IActionResult> OnPostBuyGoldAsync(uint guid, CancellationToken ct)
    {
        if (!await LoadOwnAsync(guid, ct))
            return NotFound();
        if (!ModelState.IsValid)
            return Page();

        var added = (ulong)Gold.Amount * 10000;
        var newMoney = Math.Min((ulong)Character.Money + added, uint.MaxValue);
        await characters.SetMoneyAsync(guid, (uint)newMoney, ct);
        Flash = $"Начислено {Gold.Amount} золота. Изменения вступят в силу при следующем входе в игру.";
        return RedirectToPage(new { guid });
    }

    private async Task<bool> LoadOwnAsync(uint guid, CancellationToken ct)
    {
        var character = await characters.GetByGuidAsync(guid, ct);
        // Доступ только к своим персонажам — иначе чужой аккаунт мог бы листать по guid.
        if (character is null || character.AccountId != User.AccountId())
            return false;

        var items = await inventory.GetItemsAsync(guid, ct);
        Card = new CharacterCardModel { Character = character, Equipment = CharacterCardModel.BuildEquipment(items) };
        AvailableRaces = GameData.RacesForClassSameFaction(character.Class, character.Race);
        return true;
    }
}
