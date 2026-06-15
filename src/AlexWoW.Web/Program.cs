using AlexWoW.Database;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Repositories;
using AlexWoW.Database.Repositories.World;
using AlexWoW.Web;
using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture));

builder.Services.Configure<WebOptions>(builder.Configuration.GetSection(WebOptions.SectionName));

// Та же БД alexwow_auth, что и у игровых серверов. Контекст из пула на операцию (singleton-safe),
// фиксированная ServerVersion — без коннекта при старте DI. Миграции применяет AuthServer (единый
// мигратор), панель только читает/пишет аккаунты и читает персонажей.
builder.Services.AddPooledDbContextFactory<AuthDbContext>((sp, o) =>
{
    var options = sp.GetRequiredService<IOptions<WebOptions>>().Value;
    o.UseMySql(options.ConnectionString, ServerVersion.Create(new Version(8, 4, 0), ServerType.MySql));
});
builder.Services.AddSingleton<IAccountRepository, EfAccountRepository>();
builder.Services.AddSingleton<ICharacterRepository, EfCharacterRepository>();
builder.Services.AddSingleton<IInventoryRepository, EfInventoryRepository>();
builder.Services.AddSingleton<ISpellTestRepository, EfSpellTestRepository>(); // M12 Spell QA: чтение/анализ захвата
builder.Services.AddSingleton<ISettingRepository, EfSettingRepository>(); // M8.6: настройки сервера (стоимости)
// Поиск предметов в админке (БД мира mangos, Dapper read-only). Отдельная строка подключения.
builder.Services.AddSingleton<IItemSearchRepository>(sp => new ItemSearchRepository(
    sp.GetRequiredService<IOptions<WebOptions>>().Value.WorldConnectionString));
builder.Services.AddSingleton<VikunjaTicketService>(); // M12 Spell QA: заведение тикета по аномалиям
builder.Services.AddSingleton<ServerSettingsService>(); // M8.6: типизированный доступ к стоимостям
builder.Services.AddSingleton<ProjectDashboardService>();  // Дашборд: срез 1 — БД project (прогресс)
builder.Services.AddSingleton<VikunjaDashboardService>();  // Дашборд: срез 2 — трекер Vikunja (P01..P40)
builder.Services.AddScoped<IAccountService, AccountService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Login";
        o.LogoutPath = "/Logout";
        o.AccessDeniedPath = "/Login";
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
        o.Cookie.Name = "AlexWoW.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
    });
builder.Services.AddAuthorization(o =>
    // M12: админ-раздел панели — только аккаунтам is_admin=1 (claim выставляется при входе).
    o.AddPolicy("Admin", p => p.RequireClaim(AuthSessionExtensions.AdminClaim, "1")));

// За Caddy (терминирует TLS) — доверяем заголовкам X-Forwarded-* для корректных https-ссылок.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddRazorPages(o =>
{
    // Все страницы требуют входа, кроме явно открытых (главная, вход, регистрация).
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AuthorizeFolder("/Admin", "Admin"); // M12: раздел только для администраторов
    o.Conventions.AllowAnonymousToPage("/Index");
    o.Conventions.AuthorizePage("/Dashboard", "Admin"); // дашборд прогресса — только админам
    o.Conventions.AllowAnonymousToPage("/Login");
    o.Conventions.AllowAnonymousToPage("/Register");
    o.Conventions.AllowAnonymousToPage("/Error");
});

var app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseSerilogRequestLogging();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();

/// <summary>Точка входа как public partial — чтобы WebApplicationFactory&lt;Program&gt; видел сборку в тестах.</summary>
public partial class Program;
