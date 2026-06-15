using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlexWoW.Web.Tests;

/// <summary>Страница «Предметы» (админка): поиск выполняется по фильтру, результаты с тултипом рендерятся.</summary>
public sealed class ItemsRenderTests
{
    private static readonly ItemTemplateData SampleItem = new()
    {
        Entry = 23456,
        Name = "Gladiator's Plate Legguards",
        Quality = 4,            // эпическое
        Class = 4,              // доспех
        SubClass = 4,           // латы
        InventoryType = 7,      // ноги
        RequiredLevel = 70,
        ItemLevel = 154,
        Armor = 1100,
        SellPrice = 12345,
        MaxDurability = 100,
        Stats = [new ItemStat(4, 50), new ItemStat(7, 60)], // +50 силы, +60 выносливости
    };

    [Fact]
    public async Task Admin_search_renders_results_with_tooltip()
    {
        using var factory = new RenderFactory([SampleItem], isAdmin: true);
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/Admin/Items?Name=Gladiator");

        Assert.NotNull(factory.Repo.LastFilter);
        Assert.Equal("Gladiator", factory.Repo.LastFilter!.NameContains);
        Assert.Contains("23456", html);                                  // entry
        Assert.Contains(ItemDisplay.QualityColor(4), html);             // цвет качества (эпик)
        Assert.Contains("item-tip", html);                              // блок тултипа
        Assert.Contains("Броня: 1100", html);                          // тултип содержит броню
    }

    [Fact]
    public async Task No_filter_does_not_search()
    {
        using var factory = new RenderFactory([SampleItem], isAdmin: true);
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/Admin/Items");

        Assert.Null(factory.Repo.LastFilter);                           // поиск не вызывался
        Assert.DoesNotContain("item-table", html);                     // таблицы результатов нет
    }

    [Fact]
    public async Task Non_admin_is_denied()
    {
        using var factory = new RenderFactory([SampleItem], isAdmin: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/Admin/Items?Name=Gladiator");

        // Аутентифицирован, но политика Admin не выполнена → доступ запрещён (403).
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Null(factory.Repo.LastFilter); // обработчик страницы не вызывался
    }

    private sealed class RenderFactory(IReadOnlyList<ItemTemplateData> items, bool isAdmin)
        : WebApplicationFactory<Program>
    {
        public FakeItemSearchRepository Repo { get; } = new(items);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IItemSearchRepository>();
                services.AddSingleton<IItemSearchRepository>(Repo);

                services.AddSingleton(new TestAuthState(isAdmin));
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            });
        }
    }

    private sealed record TestAuthState(bool IsAdmin);

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
        TestAuthState state)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "1") };
            if (state.IsAdmin)
                claims.Add(new Claim("alexwow:is_admin", "1")); // AuthSessionExtensions.AdminClaim (internal)
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
