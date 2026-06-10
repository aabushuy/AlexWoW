using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages;

/// <summary>Выход с сайта (только POST — защита от CSRF-logout по ссылке).</summary>
public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAccountAsync();
        return RedirectToPage("/Index");
    }
}
