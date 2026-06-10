using System.ComponentModel.DataAnnotations;
using AlexWoW.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages.Account;

public sealed class ChangePasswordModel(IAccountService accounts) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool Success { get; set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Введите текущий пароль.")]
        [DataType(DataType.Password)]
        [Display(Name = "Текущий пароль")]
        public string CurrentPassword { get; set; } = "";

        [Required(ErrorMessage = "Введите новый пароль.")]
        [StringLength(16, MinimumLength = 4, ErrorMessage = "Пароль должен быть от 4 до 16 символов.")]
        [DataType(DataType.Password)]
        [Display(Name = "Новый пароль")]
        public string NewPassword { get; set; } = "";

        [Required(ErrorMessage = "Повторите новый пароль.")]
        [Compare(nameof(NewPassword), ErrorMessage = "Пароли не совпадают.")]
        [DataType(DataType.Password)]
        [Display(Name = "Повтор нового пароля")]
        public string ConfirmPassword { get; set; } = "";
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Page();

        var email = AuthSession.Email(User);
        var ok = await accounts.ChangePasswordAsync(email, Input.CurrentPassword, Input.NewPassword, ct);
        if (!ok)
        {
            ModelState.AddModelError(string.Empty, "Текущий пароль неверен.");
            return Page();
        }

        Success = true;
        ModelState.Clear();
        Input = new InputModel();
        return Page();
    }
}
