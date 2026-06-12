using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ModelAccount = AlexWoW.Database.Models.Account;

namespace AlexWoW.Web.Pages.Admin.Accounts;

/// <summary>
/// Карточка аккаунта (админ, M8.9): инфо + сброс пароля на стандартный + персонажи с кнопками
/// «Удалить»/«Редактировать». Доступ — только админам (раздел /Admin под политикой Admin).
/// </summary>
public sealed class EditModel(
    IAccountRepository accounts,
    ICharacterRepository characters,
    IAccountService accountService) : PageModel
{
    public ModelAccount Account { get; private set; } = null!;
    public IReadOnlyList<Character> Characters { get; private set; } = [];

    /// <summary>Флеш-сообщение об успехе операции (TempData переживает redirect).</summary>
    [TempData]
    public string? Flash { get; set; }

    public async Task<IActionResult> OnGetAsync(uint id, CancellationToken ct)
    {
        if (!await LoadAsync(id, ct))
            return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(uint id, CancellationToken ct)
    {
        var account = await accounts.GetAccountByIdAsync(id, ct);
        if (account is null)
            return NotFound();

        await accountService.AdminResetPasswordAsync(account.Username, ct);
        Flash = $"Пароль аккаунта «{account.Username}» сброшен на «{AccountService.DefaultResetPassword}».";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteCharacterAsync(uint id, uint guid, CancellationToken ct)
    {
        // DeleteAsync ограничен accountId — нельзя снести чужого персонажа мимо этого аккаунта.
        var deleted = await characters.DeleteAsync(guid, id, ct);
        Flash = deleted ? "Персонаж удалён." : "Персонаж не найден (уже удалён?).";
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(uint id, CancellationToken ct)
    {
        var account = await accounts.GetAccountByIdAsync(id, ct);
        if (account is null)
            return false;
        Account = account;
        Characters = await characters.GetByAccountAsync(id, ct);
        return true;
    }
}
