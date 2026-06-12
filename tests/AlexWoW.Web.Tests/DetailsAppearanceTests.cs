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
}
