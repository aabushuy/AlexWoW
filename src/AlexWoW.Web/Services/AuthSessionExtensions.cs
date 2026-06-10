using System.Globalization;
using System.Security.Claims;
using AlexWoW.Database.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AlexWoW.Web.Services;

/// <summary>
/// Расширения cookie-сессии панели: выдача/снятие аутентификации по игровому аккаунту
/// и чтение claims текущего пользователя.
/// </summary>
internal static class AuthSessionExtensions
{
    /// <summary>Имя игрового логина в claims (для подсказки «что вводить в клиенте»).</summary>
    public const string GameAccountClaim = "alexwow:game_account";

    /// <summary>account_id → NameIdentifier, email → Name, игровой логин → GameAccountClaim.</summary>
    public static async Task SignInAccountAsync(this HttpContext http, Account account, bool persistent = true)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, account.Email ?? account.Username),
            new(GameAccountClaim, account.Username),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = persistent });
    }

    /// <summary>Снимает cookie-аутентификацию панели (выход с сайта).</summary>
    public static Task SignOutAccountAsync(this HttpContext http) =>
        http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    /// <summary>account_id текущего пользователя из claims (0, если не аутентифицирован).</summary>
    public static uint AccountId(this ClaimsPrincipal user) =>
        uint.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    /// <summary>Email текущего пользователя (логин на сайте).</summary>
    public static string Email(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name) ?? "";

    /// <summary>Игровой логин (то, что игрок вводит в клиенте WoW).</summary>
    public static string GameAccount(this ClaimsPrincipal user) => user.FindFirstValue(GameAccountClaim) ?? "";
}
