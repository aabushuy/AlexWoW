using System.ComponentModel.DataAnnotations;
using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages;

/// <summary>Вход на сайт по email/паролю (SRP-проверка по игровому логину).</summary>
public sealed class LoginModel(IAccountService accounts) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Укажите email.")]
        [EmailAddress(ErrorMessage = "Некорректный email.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Укажите пароль.")]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; } = "";

        [Display(Name = "Запомнить меня")]
        public bool RememberMe { get; set; } = true;
    }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl, CancellationToken ct)
    {
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid)
            return Page();

        var account = await accounts.VerifyCredentialsAsync(Input.Email, Input.Password, ct);
        if (account is null)
        {
            ModelState.AddModelError(string.Empty, "Неверный email или пароль.");
            return Page();
        }

        await HttpContext.SignInAccountAsync(account, Input.RememberMe);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectToPage("/Characters/Index");
    }
}
