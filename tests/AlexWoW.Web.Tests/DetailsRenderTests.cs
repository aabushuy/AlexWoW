using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlexWoW.Web.Tests;

/// <summary>
/// Рендер страницы персонажа (M8.6): селект расы должен быть НЕ disabled, когда классу/фракции
/// доступно несколько рас. Регрессия бага «раса не меняется» — disabled-селект не отправлял значение.
/// </summary>
public sealed class DetailsRenderTests
{
    [Fact]
    public async Task Race_select_is_enabled_when_multiple_races_available()
    {
        // Маг-Человек (Альянс): доступны Человек/Гном/Дреней — смена расы возможна.
        var character = new Character { Guid = 10, AccountId = 1, Name = "Маг", Race = 1, Class = 8, Gender = 0, Money = 100_000_000 };
        using var factory = new RenderFactory(character);
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/Characters/Details/10");

        var select = Regex.Match(html, "<select[^>]*name=\"Appearance\\.Race\"[^>]*>");
        Assert.True(select.Success, "Селект расы не найден на странице");
        Assert.DoesNotContain("disabled", select.Value);     // ключевая проверка: поле включено → значение отправится
        // В дропдауне есть альтернативные расы (Маг-Альянс: Гном=7, Дреней=11). Кириллица кодируется
        // числовыми entity, поэтому проверяем по value опций, а не по тексту.
        Assert.Contains("<option value=\"7\"", html);
        Assert.Contains("<option value=\"11\"", html);
    }

    private sealed class RenderFactory(Character character) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICharacterRepository>();
                services.AddSingleton<ICharacterRepository>(new FakeCharacterRepository(character));
                services.RemoveAll<IInventoryRepository>();
                services.AddSingleton<IInventoryRepository, EmptyInventory>();
                services.RemoveAll<ISettingRepository>();
                services.AddSingleton<ISettingRepository, EmptySettings>();

                // Аутентифицируем все запросы тестовым аккаунтом id=1 (совпадает с владельцем персонажа).
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            });
        }
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim("alexwow:game_account", "TEST")], "Test");
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
