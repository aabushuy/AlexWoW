using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages.Admin.Accounts;

/// <summary>Список всех аккаунтов с числом персонажей (админ, M8.9). Клик по строке → карточка аккаунта.</summary>
public sealed class IndexModel(IAccountRepository accounts) : PageModel
{
    public IReadOnlyList<AccountSummary> Accounts { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
        => Accounts = await accounts.GetAccountsWithCharCountsAsync(ct);
}
