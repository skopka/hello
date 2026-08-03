using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Identity.Authentication;

namespace Skopka.Hello.UI.Pages;

public sealed class RegisterModel(IHelloUiApplication application)
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
                result.Errors);
            return Page();
        }

        return RedirectToPage(
            "/SkopkaHello/Login",
            new { registered = true });
    }

    public sealed class InputModel : IValidatableObject
    {
        [Required]
        [StringLength(200)]
        [Display(Name = "Display name")]
        public string DisplayName { get; set; } = string.Empty;

        [EmailAddress]
        [StringLength(320)]
        public string? Email { get; set; }

        [StringLength(100)]
        [Display(Name = "User name")]
        public string? UserName { get; set; }

        [StringLength(IdentityLoginLimits.MaximumLoginLength)]
        public string? Phone { get; set; }

        [StringLength(32)]
        public string? Locale { get; set; }

        [Required]
        [StringLength(128)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(
            nameof(Password),
            ErrorMessage = "The passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        public IEnumerable<ValidationResult> Validate(
            ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(UserName)
                && string.IsNullOrWhiteSpace(Email)
                && string.IsNullOrWhiteSpace(Phone))
            {
                yield return new ValidationResult(
                    "Enter a user name, email address or phone number.",
                    [nameof(UserName), nameof(Email), nameof(Phone)]);
            }
        }
    }
}
