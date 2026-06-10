using System.ComponentModel.DataAnnotations;
using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages;

public sealed class RegisterModel(IAccountService accounts) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Укажите email.")]
        [EmailAddress(ErrorMessage = "Некорректный email.")]
        [StringLength(255, ErrorMessage = "Email слишком длинный.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Укажите пароль.")]
        [StringLength(16, MinimumLength = 4, ErrorMessage = "Пароль должен быть от 4 до 16 символов.")]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Повторите пароль.")]
        [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают.")]
        [DataType(DataType.Password)]
        [Display(Name = "Повтор пароля")]
        public string ConfirmPassword { get; set; } = "";
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Page();

        var result = await accounts.RegisterAsync(Input.Email, Input.Password, ct);
        if (result == RegisterResult.AlreadyExists)
        {
            ModelState.AddModelError(string.Empty, "Аккаунт с таким email уже существует.");
            return Page();
        }

        // После регистрации сразу входим под новым аккаунтом.
        var account = await accounts.VerifyCredentialsAsync(Input.Email, Input.Password, ct);
        if (account is not null)
            await AuthSession.SignInAsync(HttpContext, account);

        return RedirectToPage("/Characters/Index");
    }
}
