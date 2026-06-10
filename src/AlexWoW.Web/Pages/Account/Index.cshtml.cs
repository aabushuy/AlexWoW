using AlexWoW.Database.Abstractions;
using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages.Account;

/// <summary>Сводка аккаунта: игровой логин, email, дата регистрации.</summary>
public sealed class IndexModel(IAccountRepository accounts) : PageModel
{
    public string GameAccount { get; private set; } = "";
    public string Email { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var account = await accounts.GetAccountByEmailAsync(User.Email(), ct);
        if (account is null)
            return RedirectToPage("/Logout");

        GameAccount = account.Username;
        Email = account.Email ?? "—";
        CreatedAt = account.CreatedAt;
        return Page();
    }
}
