using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Identity.Authentication;

namespace Skopka.Hello.UI.Pages;

public sealed class RegisterModel(
    IHelloUiApplication application,
    IHelloUiLocalizer text)
    : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await application.RegisterAsync(
            new HelloUiRegisterCommand(
                Input.UserName,
                Input.Email,
                Input.Phone,
                Input.DisplayName,
                Input.Locale,
                Input.Password),
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors,
                text);
            return Page();
        }

        return RedirectToPage(
            "/SkopkaHello/Login",
            new { registered = true });
    }

    public sealed class InputModel : IValidatableObject
    {
        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(200, ErrorMessage = "Validation.StringLength")]
        [Display(Name = "Field.DisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Validation.EmailAddress")]
        [StringLength(320, ErrorMessage = "Validation.StringLength")]
        public string? Email { get; set; }

        [StringLength(100, ErrorMessage = "Validation.StringLength")]
        [Display(Name = "Field.UserName")]
        public string? UserName { get; set; }

        [StringLength(
            IdentityLoginLimits.MaximumLoginLength,
            ErrorMessage = "Validation.StringLength")]
        public string? Phone { get; set; }

        [StringLength(32, ErrorMessage = "Validation.StringLength")]
        public string? Locale { get; set; }

        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(128, ErrorMessage = "Validation.StringLength")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Validation.Required")]
        [DataType(DataType.Password)]
        [Compare(
            nameof(Password),
            ErrorMessage = "Validation.PasswordsDoNotMatch")]
        [Display(Name = "Field.ConfirmPassword")]
        public string ConfirmPassword { get; set; } = string.Empty;

        public IEnumerable<ValidationResult> Validate(
            ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(UserName)
                && string.IsNullOrWhiteSpace(Email)
                && string.IsNullOrWhiteSpace(Phone))
            {
                var localizer = validationContext.GetService(
                    typeof(IHelloUiLocalizer)) as IHelloUiLocalizer;
                yield return new ValidationResult(
                    localizer?["Validation.LoginIdentifierRequired"]
                        .Value
                    ?? "Enter a user name, email address or phone number.",
                    [nameof(UserName), nameof(Email), nameof(Phone)]);
            }
        }
    }
}
