using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

public sealed class ResetPasswordModel(
    IHelloUiApplication application,
    IHelloSessionCookieManager sessionCookies,
    IHelloUiLocalizer text)
    : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = string.Empty;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool LinkValid { get; private set; }

    public void OnGet()
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        LinkValid = IsLinkValid();
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        LinkValid = IsLinkValid();
        if (!LinkValid)
        {
            ModelState.AddModelError(
                string.Empty,
                text["ResetPassword.InvalidLink"]);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await application.ResetPasswordAsync(
            new HelloUiResetPasswordCommand(
                UserId,
                Token,
                Input.NewPassword),
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
                    ? "Input.NewPassword"
                    : $"Input.{field}");
            return Page();
        }

        sessionCookies.DeleteSessionCookies(HttpContext);
        await HttpContext.SignOutAsync(
            HelloUiDefaults.AuthenticationScheme);
        return RedirectToPage(
            "/SkopkaHello/Login",
            new { passwordReset = true });
    }

    private bool IsLinkValid()
        => UserId != Guid.Empty
            && !string.IsNullOrWhiteSpace(Token);

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(128, ErrorMessage = "Validation.StringLength")]
        [DataType(DataType.Password)]
        [Display(Name = "Field.NewPassword")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Validation.Required")]
        [DataType(DataType.Password)]
        [Compare(
            nameof(NewPassword),
            ErrorMessage = "Validation.PasswordsDoNotMatch")]
        [Display(Name = "Field.ConfirmPassword")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
