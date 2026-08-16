using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Identity.Authentication;

namespace Skopka.Hello.UI.Pages;

[method: ActivatorUtilitiesConstructor]
public sealed class RegisterModel(
    IHelloUiApplication application,
    IHelloUiLocalizer text,
    SkopkaHelloUiOptions uiOptions)
    : PageModel
{
    public RegisterModel(
        IHelloUiApplication application,
        IHelloUiLocalizer text)
        : this(application, text, new SkopkaHelloUiOptions())
    {
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public HelloUiRegistrationOptions Registration =>
        uiOptions.Registration;

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        var values = HelloUiRegistrationFormValidator.Validate(
            Registration,
            ModelState,
            text,
            Input.DisplayName,
            Input.Email,
            Input.UserName,
            Input.Phone,
            Input.Locale,
            requireLoginIdentifier: true);
        Input.DisplayName = values.DisplayName;
        Input.Email = values.Email;
        Input.UserName = values.UserName;
        Input.Phone = values.Phone;
        Input.Locale = values.Locale;
        HelloUiLegalConsentValidator.Validate(
            uiOptions,
            ModelState,
            text,
            Input.AcceptTermsOfService,
            Input.AcceptPrivacyPolicy);
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
                text,
                field => String.Equals(
                        field,
                        "newPassword",
                        StringComparison.OrdinalIgnoreCase)
                    ? "Input.Password"
                    : $"Input.{field}");
            return Page();
        }

        return RedirectToPage(
            "/SkopkaHello/Login",
            new { registered = true });
    }

    public sealed class InputModel
    {
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

        public bool AcceptTermsOfService { get; set; }

        public bool AcceptPrivacyPolicy { get; set; }
    }
}
