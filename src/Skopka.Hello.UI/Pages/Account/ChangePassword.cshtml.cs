using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class ChangePasswordModel(
    IHelloUiApplication application,
    IHelloSessionCookieManager sessionCookies)
    : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool CodeRequested =>
        Input.ChallengeId != Guid.Empty;

    public bool RestartRequired { get; private set; }

    public DateTimeOffset? CodeExpiresAt { get; private set; }

    public void OnGet()
        => HelloUiSensitivePage.ApplyResponseHeaders(Response);

    public async Task<IActionResult> OnPostRequestCodeAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        ModelState.Clear();

        var result = await application.BeginPasswordChangeAsync(
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors);
            return Page();
        }

        Input.ChallengeId = result.Value.ChallengeId;
        CodeExpiresAt = result.Value.ExpiresAt;
        return Page();
    }

    public async Task<IActionResult> OnPostChangeAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (Input.ChallengeId == Guid.Empty)
        {
            ModelState.AddModelError(
                string.Empty,
                "Request a verification code before changing the password.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await application.CompletePasswordChangeAsync(
            new HelloUiCompletePasswordChangeCommand(
                Input.ChallengeId,
                Input.VerificationCode,
                Input.CurrentPassword,
                Input.NewPassword),
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.Errors.Any(error => string.Equals(
                    error.Code,
                    HelloPasswordChangeErrorCodes
                        .SessionCleanupRequired,
                    StringComparison.Ordinal)))
            {
                Input = new InputModel();
                ModelState.Clear();
                sessionCookies.DeleteSessionCookies(HttpContext);
                await HttpContext.SignOutAsync(
                    HelloUiDefaults.AuthenticationScheme);
                return RedirectToPage(
                    "/SkopkaHello/Login",
                    new { passwordChangedSessionCleanup = true });
            }

            var restart = result.Errors.FirstOrDefault(error =>
                string.Equals(
                    error.Code,
                    HelloPasswordChangeErrorCodes.RestartRequired,
                    StringComparison.Ordinal));
            if (restart is null)
            {
                HelloUiModelState.AddErrors(
                    ModelState,
                    result.Errors);
                return Page();
            }

            Input = new InputModel();
            ModelState.Clear();
            RestartRequired = true;
            ModelState.AddModelError(string.Empty, restart.Message);
            foreach (var cause in result.Errors.Where(error =>
                !string.Equals(
                    error.Code,
                    HelloPasswordChangeErrorCodes.RestartRequired,
                    StringComparison.Ordinal)))
            {
                ModelState.AddModelError(
                    string.Empty,
                    cause.Message);
            }

            return Page();
        }

        sessionCookies.DeleteSessionCookies(HttpContext);
        await HttpContext.SignOutAsync(
            HelloUiDefaults.AuthenticationScheme);
        return RedirectToPage(
            "/SkopkaHello/Login",
            new { passwordReset = true });
    }

    public sealed class InputModel
    {
        public Guid ChallengeId { get; set; }

        [Required]
        [StringLength(8, MinimumLength = 6)]
        [Display(Name = "Verification code")]
        public string VerificationCode { get; set; } = string.Empty;

        [Required]
        [StringLength(128)]
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = string.Empty;

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
