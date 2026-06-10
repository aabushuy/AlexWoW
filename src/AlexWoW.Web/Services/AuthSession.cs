using System.Security.Claims;
using AlexWoW.Database.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AlexWoW.Web.Services;

/// <summary>Помощник cookie-сессии: выдаёт/снимает аутентификацию по игровому аккаунту.</summary>
public static class AuthSession
{
    /// <summary>account_id хранится в claim NameIdentifier, email — в Name.</summary>
    public static async Task SignInAsync(HttpContext http, Account account, bool persistent = true)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Name, account.Username),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = persistent });
    }

    public static Task SignOutAsync(HttpContext http) =>
        http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    /// <summary>account_id текущего пользователя из claims (0, если не аутентифицирован).</summary>
    public static uint AccountId(ClaimsPrincipal user) =>
        uint.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public static string Email(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name) ?? "";
}
