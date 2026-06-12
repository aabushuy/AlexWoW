using System.Security.Claims;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.Web.Pages.Characters;
using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace AlexWoW.Web.Tests;

/// <summary>
/// Регрессия: на странице персонажа две формы с общей моделью. Пустое поле Gold.Amount второй формы
/// не должно блокировать смену расы/пола (баг «раса не меняется и ошибки нет»). M8.6.
/// </summary>
public sealed class DetailsAppearanceTests
{
    [Fact]
    public async Task Appearance_change_not_blocked_by_empty_gold_field()
    {
        // Воин-Человек (Альянс), 10000 золота. Меняем расу на Дворфа (3) — валидно, хватает денег.
        var character = MakeCharacter(race: 1, cls: 1, gender: 0, money: 100_000_000);
        var repo = new FakeCharacterRepository(character);
        var model = BuildModel(repo, accountId: character.AccountId);

        // Имитируем то, что делает биндер: пустое Gold.Amount нарушает [Range(1,..)].
        model.ModelState.AddModelError("Gold.Amount", "Введите от 1 до 100000 золота.");
        model.Appearance = new DetailsModel.AppearanceInput { Race = 3, Gender = 0 };

        var result = await model.OnPostAppearanceAsync(character.Guid, default);

        Assert.IsType<RedirectToPageResult>(result);          // не вернулись на Page() с ошибкой
        Assert.Equal((byte)3, repo.SetRaceGenderRace);         // раса реально изменена
        Assert.Equal(100_000_000u - 10_000_000u, repo.SetMoneyValue); // списано 1000 золота
    }

    [Fact]
    public async Task Appearance_change_blocked_when_not_enough_gold()
    {
        // Денег только 500 золота — на смену расы (1000) не хватает.
        var character = MakeCharacter(race: 1, cls: 1, gender: 0, money: 5_000_000);
        var repo = new FakeCharacterRepository(character);
        var model = BuildModel(repo, accountId: character.AccountId);
        model.Appearance = new DetailsModel.AppearanceInput { Race = 3, Gender = 0 };

        var result = await model.OnPostAppearanceAsync(character.Guid, default);

        Assert.IsType<PageResult>(result);                     // показали страницу с ошибкой
        Assert.False(model.ModelState.IsValid);
        Assert.Null(repo.SetMoneyValue);                       // ничего не списали
        Assert.Null(repo.SetRaceGenderRace);                   // расу не меняли
    }

    private static DetailsModel BuildModel(ICharacterRepository repo, uint accountId)
    {
        var settings = new ServerSettingsService(new EmptySettings());   // дефолты 1000/2000
        var model = new DetailsModel(repo, new EmptyInventory(), settings);

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, accountId.ToString())], "test")),
        };
        var actionContext = new ActionContext(http, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        model.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), actionContext.ModelState),
        };
        return model;
    }

    private static Character MakeCharacter(byte race, byte cls, byte gender, uint money) => new()
    {
        Guid = 10,
        AccountId = 1,
        Name = "Тест",
        Race = race,
        Class = cls,
        Gender = gender,
        Money = money,
    };

    private sealed class FakeCharacterRepository(Character character) : ICharacterRepository
    {
        public byte? SetRaceGenderRace { get; private set; }
        public uint? SetMoneyValue { get; private set; }

        public Task<Character?> GetByGuidAsync(uint guid, CancellationToken ct = default) =>
            Task.FromResult<Character?>(guid == character.Guid ? character : null);

        public Task SetRaceGenderAsync(uint guid, byte race, byte gender, CancellationToken ct = default)
        {
            SetRaceGenderRace = race;
            return Task.CompletedTask;
        }

        public Task SetMoneyAsync(uint guid, uint money, CancellationToken ct = default)
        {
            SetMoneyValue = money;
            return Task.CompletedTask;
        }

        // Не используются в этих тестах.
        public Task EnsureSchemaAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Character>> GetByAccountAsync(uint accountId, CancellationToken ct = default) => throw new NotImplementedException();
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
        public Task<bool> DeleteAsync(uint guid, uint accountId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class EmptyInventory : IInventoryRepository
    {
        public Task<IReadOnlyList<InventoryItem>> GetItemsAsync(uint ownerGuid, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<InventoryItem>>([]);
        public Task<bool> HasItemsAsync(uint ownerGuid, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<uint> AddItemAsync(uint ownerGuid, uint itemEntry, byte bag, byte slot, uint stackCount = 1, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveItemAsync(uint itemGuid, CancellationToken ct = default) => throw new NotImplementedException();
        public Task MoveItemAsync(uint itemGuid, byte bag, byte slot, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetItemStackAsync(uint itemGuid, uint stackCount, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class EmptySettings : ISettingRepository
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }
}
