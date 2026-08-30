using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class ChangePasswordModel(
    IHelloUiApplication application,
    IHelloSessionCookieManager sessionCookies,
    IHelloUiLocalizer text)
    : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public Guid ChallengeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public HelloDeliveryChannel? DeliveryChannel { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? ExpiresAt { get; set; }

    public bool CodeRequested =>
        Input.ChallengeId != Guid.Empty;

    public bool RestartRequired { get; private set; }

    public DateTimeOffset? CodeExpiresAt { get; private set; }

    public HelloDeliveryChannel? VerificationChannel { get; private set; }

    public void OnGet()
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (ChallengeId == Guid.Empty
            || DeliveryChannel is null
            || !Enum.IsDefined(DeliveryChannel.Value))
        {
            return;
        }

        Input.ChallengeId = ChallengeId;
        Input.DeliveryChannel = DeliveryChannel.Value;
        VerificationChannel = DeliveryChannel;
        CodeExpiresAt = ExpiresAt;
    }

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
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors,
                text);
            return Page();
        }

        return RedirectToPage(
            "/SkopkaHello/Account/ChangePassword",
            new
            {
                challengeId = result.Value.ChallengeId,
                deliveryChannel = result.Value.DeliveryChannel,
                expiresAt = result.Value.ExpiresAt,
            });
    }

    public async Task<IActionResult> OnPostChangeAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        VerificationChannel = Enum.IsDefined(Input.DeliveryChannel)
            ? Input.DeliveryChannel
            : null;
        if (Input.ChallengeId == Guid.Empty)
        {
            ModelState.AddModelError(
                string.Empty,
                text["ChangePassword.RequestCodeFirst"]);
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

            Input = new InputModel();
            ModelState.Clear();
            RestartRequired = true;
            ModelState.AddModelError(
                string.Empty,
                LocalizeError(restart));
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors.Where(error =>
                        !string.Equals(
                            error.Code,
                            HelloPasswordChangeErrorCodes.RestartRequired,
                            StringComparison.Ordinal))
                    .ToArray(),
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

    public sealed class InputModel
    {
        public Guid ChallengeId { get; set; }

        public HelloDeliveryChannel DeliveryChannel { get; set; }

        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(
            64,
            MinimumLength = 6,
            ErrorMessage = "Validation.StringLengthRange")]
        [Display(Name = "Field.VerificationCode")]
        public string VerificationCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(128, ErrorMessage = "Validation.StringLength")]
        [DataType(DataType.Password)]
        [Display(Name = "Field.CurrentPassword")]
        public string CurrentPassword { get; set; } = string.Empty;

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

    private string LocalizeError(
        Skopka.Abstraction.OperationResult.Error error)
        => text.TryGetString(
            $"Errors.{error.Code}",
            out var message)
                ? message
                : error.Message;
}
