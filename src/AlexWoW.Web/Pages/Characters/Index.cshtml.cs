using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages.Characters;

/// <summary>Список персонажей текущего аккаунта.</summary>
public sealed class IndexModel(ICharacterRepository characters) : PageModel
{
    public IReadOnlyList<Character> Characters { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var accountId = User.AccountId();
        Characters = await characters.GetByAccountAsync(accountId, ct);
    }
}
