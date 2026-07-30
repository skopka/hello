using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

public sealed class ResetPasswordModel(
    IHelloUiApplication application,
    IHelloSessionCookieManager sessionCookies)
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
                "This password reset link is incomplete or invalid.");
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
            HelloUiModelState.AddErrors(ModelState, result.Errors);
            return Page();
        }

        sessionCookies.DeleteSessionCookies(HttpContext);
        await HttpContext.SignOutAsync(
            HelloUiDefaults.AuthenticationScheme);
        return RedirectToPage(
            "/Login",
            new { passwordReset = true });
    }

    private bool IsLinkValid()
        => UserId != Guid.Empty
            && !string.IsNullOrWhiteSpace(Token);

    public sealed class InputModel
    {
        [Required]
        [StringLength(128)]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(
            nameof(NewPassword),
            ErrorMessage = "The passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
