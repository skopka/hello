using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class AccountSecurityModel(
    IHelloUiApplication application,
    IHelloSessionCookieManager sessionCookies,
    HelloUiPrgStateStore prgState,
    IHelloUiLocalizer text,
    IHelloUiWebAuthnApplication? passkeys = null)
    : PageModel
{

    /// <summary>
    /// The keys on this account, or null where the host has no passkeys at all
    /// — which the page reads as "do not mention them" rather than "none".
    /// </summary>
    public IReadOnlyList<HelloUiWebAuthnCredential>? Passkeys
    {
        get;
        private set;
    }

    public bool PasskeysEnabled => passkeys is not null;

    [BindProperty]
    public PasskeyInput Passkey { get; set; } = new();

    public async Task<IActionResult> OnPostPasskeyChallengeAsync(
        CancellationToken cancellationToken)
    {
        if (passkeys is null)
        {
            return NotFound();
        }

        var issued = await passkeys.BeginRegistrationAsync(
            HttpContext,
            cancellationToken);
        return issued.IsSuccess ? new JsonResult(issued.Value) : StatusCode(400);
    }

    public async Task<IActionResult> OnPostAddPasskeyAsync(
        CancellationToken cancellationToken)
    {
        if (passkeys is null)
        {
            return NotFound();
        }

        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        var registered = await passkeys.RegisterAsync(
            new HelloUiWebAuthnAttestation(
                Passkey.Ticket,
                Passkey.ClientDataJson,
                Passkey.AttestationObject,
                Passkey.Label),
            HttpContext,
            cancellationToken);
        if (registered.IsSuccess)
        {
            return RedirectToPage(new { passkeyAdded = true });
        }

        HelloUiModelState.AddErrors(ModelState, registered.Errors, text);
        return await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRemovePasskeyAsync(
        Guid credentialId,
        CancellationToken cancellationToken)
    {
        if (passkeys is null)
        {
            return NotFound();
        }

        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        var removed = await passkeys.RemoveAsync(
            credentialId,
            HttpContext,
            cancellationToken);
        if (removed.IsSuccess)
        {
            // Removing a key rotates the security stamp, so the ticket this
            // browser is holding is no longer good: sign in again rather than
            // show a page nothing behind it will answer.
            return RedirectToPage(
                "/SkopkaHello/Login",
                new { securityChanged = true });
        }

        HelloUiModelState.AddErrors(ModelState, removed.Errors, text);
        return await LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Filled in by the page script from what the authenticator answered.
    /// </summary>
    public sealed class PasskeyInput
    {
        public string Ticket { get; set; } = string.Empty;

        public string ClientDataJson { get; set; } = string.Empty;

        public string AttestationObject { get; set; } = string.Empty;

        [StringLength(64)]
        public string? Label { get; set; }
    }
    public HelloCredentialState? Credentials { get; private set; }

    public HelloTotpState? Totp { get; private set; }

    public HelloTotpEnrollment? Enrollment { get; private set; }

    public Guid? PendingEnrollmentId { get; private set; }

    public IReadOnlyList<string>? RecoveryCodes { get; private set; }

    public PendingChallenge? Pending { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? PendingAction { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid PendingChallengeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? PendingExpiresAt { get; set; }

    [BindProperty(SupportsGet = true)]
    public HelloDeliveryChannel? PendingDeliveryChannel { get; set; }

    [TempData]
    public string? EnrollmentStateToken { get; set; }

    [TempData]
    public string? RecoveryCodesStateToken { get; set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        var loaded = await LoadAsync(cancellationToken);
        if (loaded is not PageResult)
        {
            return loaded;
        }

        RestorePrgState();
        if (PendingAction is "set" or "remove" or "delete" or "totp-disable"
            && PendingChallengeId != Guid.Empty
            && PendingDeliveryChannel is not null
            && Enum.IsDefined(PendingDeliveryChannel.Value))
        {
            Pending = new PendingChallenge(
                PendingAction,
                PendingChallengeId,
                PendingExpiresAt,
                PendingDeliveryChannel.Value);
        }

        return Page();
    }

    public Task<IActionResult> OnPostBeginSetAsync(
        CancellationToken cancellationToken)
        => BeginAsync(
            "set",
            application.BeginPasswordSetAsync(
                HttpContext,
                cancellationToken),
            cancellationToken);

    public Task<IActionResult> OnPostBeginRemoveAsync(
        CancellationToken cancellationToken)
        => BeginAsync(
            "remove",
            application.BeginPasswordRemovalAsync(
                HttpContext,
                cancellationToken),
            cancellationToken);

    public Task<IActionResult> OnPostBeginDeleteAsync(
        CancellationToken cancellationToken)
        => BeginAsync(
            "delete",
            application.BeginAccountDeletionAsync(
                HttpContext,
                cancellationToken),
            cancellationToken);

    public async Task<IActionResult> OnPostBeginTotpAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        var result = await application.BeginTotpEnrollmentAsync(
            HttpContext,
            cancellationToken);
        if (result.IsSuccess)
        {
            EnrollmentStateToken = prgState.Store(result.Value);
            return RedirectToPage(
                "/SkopkaHello/Account/Security");
        }

        HelloUiModelState.AddErrors(ModelState, result.Errors, text);

        var loaded = await LoadStateAsync(cancellationToken);
        return loaded ? Page() : Challenge();
    }

    public async Task<IActionResult> OnPostConfirmTotpAsync(
        [Bind(Prefix = "TotpInput")] TotpEnrollmentInput input,
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        PendingEnrollmentId = input.EnrollmentId;
        if (!ModelState.IsValid)
        {
            await LoadStateAsync(cancellationToken);
            return Page();
        }

        var result = await application.ConfirmTotpEnrollmentAsync(
            new HelloUiConfirmTotpEnrollmentCommand(
                input.EnrollmentId,
                input.Code),
            HttpContext,
            cancellationToken);
        if (result.IsSuccess)
        {
            prgState.Remove(EnrollmentStateToken);
            EnrollmentStateToken = null;
            RecoveryCodesStateToken = prgState.Store(
                result.Value.RecoveryCodes.ToArray());
            return RedirectToPage(
                "/SkopkaHello/Account/Security");
        }

        HelloUiModelState.AddErrors(
            ModelState,
            result.Errors,
            text,
            _ => "TotpInput.Code");

        var loaded = await LoadStateAsync(cancellationToken);
        return loaded ? Page() : Challenge();
    }

    public Task<IActionResult> OnPostBeginDisableTotpAsync(
        CancellationToken cancellationToken)
        => BeginAsync(
            "totp-disable",
            application.BeginTotpDisableAsync(
                HttpContext,
                cancellationToken),
            cancellationToken);

    public async Task<IActionResult> OnPostCompleteDisableTotpAsync(
        [Bind(Prefix = "ActionInput")] SecurityActionInput input,
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!ModelState.IsValid)
        {
            Pending = new PendingChallenge(
                "totp-disable",
                input.ChallengeId,
                null,
                input.DeliveryChannel);
            await LoadStateAsync(cancellationToken);
            return Page();
        }

        var result = await application.CompleteTotpDisableAsync(
            new HelloUiCompleteAccountSecurityActionCommand(
                input.ChallengeId,
                input.VerificationCode),
            HttpContext,
            cancellationToken);
        return await FinishAsync(
            result,
            "totp-disable",
            input.ChallengeId,
            input.DeliveryChannel,
            deleted: false,
            cancellationToken);
    }

    public async Task<IActionResult> OnPostCompleteSetAsync(
        [Bind(Prefix = "SetInput")] PasswordSetInput input,
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!ModelState.IsValid)
        {
            Pending = new PendingChallenge(
                "set",
                input.ChallengeId,
                null,
                input.DeliveryChannel);
            await LoadStateAsync(cancellationToken);
            return Page();
        }

        var result = await application.CompletePasswordSetAsync(
            new HelloUiCompletePasswordSetCommand(
                input.ChallengeId,
                input.VerificationCode,
                input.NewPassword),
            HttpContext,
            cancellationToken);
        return await FinishAsync(
            result,
            "set",
            input.ChallengeId,
            input.DeliveryChannel,
            deleted: false,
            cancellationToken);
    }

    public async Task<IActionResult> OnPostCompleteRemoveAsync(
        [Bind(Prefix = "ActionInput")] SecurityActionInput input,
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!ModelState.IsValid)
        {
            Pending = new PendingChallenge(
                "remove",
                input.ChallengeId,
                null,
                input.DeliveryChannel);
            await LoadStateAsync(cancellationToken);
            return Page();
        }

        var result = await application.CompletePasswordRemovalAsync(
            new HelloUiCompleteAccountSecurityActionCommand(
                input.ChallengeId,
                input.VerificationCode),
            HttpContext,
            cancellationToken);
        return await FinishAsync(
            result,
            "remove",
            input.ChallengeId,
            input.DeliveryChannel,
            deleted: false,
            cancellationToken);
    }

    public async Task<IActionResult> OnPostCompleteDeleteAsync(
        [Bind(Prefix = "ActionInput")] SecurityActionInput input,
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!ModelState.IsValid)
        {
            Pending = new PendingChallenge(
                "delete",
                input.ChallengeId,
                null,
                input.DeliveryChannel);
            await LoadStateAsync(cancellationToken);
            return Page();
        }

        var result = await application.CompleteAccountDeletionAsync(
            new HelloUiCompleteAccountSecurityActionCommand(
                input.ChallengeId,
                input.VerificationCode),
            HttpContext,
            cancellationToken);
        return await FinishAsync(
            result,
            "delete",
            input.ChallengeId,
            input.DeliveryChannel,
            deleted: true,
            cancellationToken);
    }

    private async Task<IActionResult> BeginAsync(
        string action,
        Task<Skopka.Abstraction.OperationResult.OperationResult<
            HelloStepUpChallenge>> pending,
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        var result = await pending;
        if (result.IsSuccess)
        {
            return RedirectToPage(
                "/SkopkaHello/Account/Security",
                new
                {
                    pendingAction = action,
                    pendingChallengeId = result.Value.ChallengeId,
                    pendingExpiresAt = result.Value.ExpiresAt,
                    pendingDeliveryChannel = result.Value.DeliveryChannel,
                });
        }

        HelloUiModelState.AddErrors(
            ModelState,
            result.Errors,
            text);

        var loaded = await LoadStateAsync(cancellationToken);
        return loaded ? Page() : Challenge();
    }

    private async Task<IActionResult> FinishAsync(
        Skopka.Abstraction.OperationResult.OperationResult result,
        string action,
        Guid challengeId,
        HelloDeliveryChannel deliveryChannel,
        bool deleted,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            if (result.Errors.Any(error => string.Equals(
                    error.Code,
                    HelloAccountSecurityActionErrorCodes
                        .SessionCleanupRequired,
                    StringComparison.Ordinal)))
            {
                sessionCookies.DeleteSessionCookies(HttpContext);
                await HttpContext.SignOutAsync(
                    HelloUiDefaults.AuthenticationScheme);
                return RedirectToPage(
                    "/SkopkaHello/Login",
                    new
                    {
                        accountDeleted = deleted,
                        accountSecurityChangedSessionCleanup = true,
                    });
            }

            if (result.Errors.Any(error => string.Equals(
                    error.Code,
                    Skopka.Identity.Errors.IdentityErrorCodes
                        .VerificationResponseInvalid,
                    StringComparison.Ordinal)))
            {
                Pending = new PendingChallenge(
                    action,
                    challengeId,
                    null,
                    deliveryChannel);
            }

            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors,
                text,
                field => String.Equals(
                        field,
                        "newPassword",
                        StringComparison.OrdinalIgnoreCase)
                    ? "SetInput.NewPassword"
                    : $"SetInput.{field}");
            var loaded = await LoadStateAsync(cancellationToken);
            return loaded ? Page() : Challenge();
        }

        sessionCookies.DeleteSessionCookies(HttpContext);
        await HttpContext.SignOutAsync(
            HelloUiDefaults.AuthenticationScheme);
        return RedirectToPage(
            "/SkopkaHello/Login",
            new { accountDeleted = deleted, securityChanged = !deleted });
    }

    private async Task<IActionResult> LoadAsync(
        CancellationToken cancellationToken)
    {
        var loaded = await LoadStateAsync(cancellationToken);
        return loaded ? Page() : Challenge();
    }

    private async Task<bool> LoadStateAsync(
        CancellationToken cancellationToken)
    {
        var result = await application.GetCredentialStateAsync(
            HttpContext,
            cancellationToken);
        if (result.IsSuccess)
        {
            Credentials = result.Value;
            var totp = await application.GetTotpStateAsync(
                HttpContext,
                cancellationToken);
            if (totp.IsSuccess)
            {
                Totp ??= totp.Value;
                if (passkeys is not null)
                {
                    var listed = await passkeys.ListAsync(
                        HttpContext,
                        cancellationToken);
                    if (listed.IsSuccess)
                    {
                        Passkeys = listed.Value;
                    }
                }

                return true;
            }

            HelloUiModelState.AddErrors(ModelState, totp.Errors, text);
            return !totp.Errors.Any(
                error => error.Type == Skopka.Abstraction.OperationResult
                    .ErrorType.Unauthorized);
        }

        HelloUiModelState.AddErrors(
            ModelState,
            result.Errors,
            text);
        return !result.Errors.Any(
            error => error.Type == Skopka.Abstraction.OperationResult
                .ErrorType.Unauthorized);
    }

    public sealed record PendingChallenge(
        string Action,
        Guid ChallengeId,
        DateTimeOffset? ExpiresAt,
        HelloDeliveryChannel DeliveryChannel);

    public sealed class PasswordSetInput
    {
        public Guid ChallengeId { get; set; }

        public HelloDeliveryChannel DeliveryChannel { get; set; }

        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(256, ErrorMessage = "Validation.StringLength")]
        [Display(Name = "Field.VerificationCode")]
        public string VerificationCode { get; set; } = string.Empty;

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

    public sealed class SecurityActionInput
    {
        public Guid ChallengeId { get; set; }

        public HelloDeliveryChannel DeliveryChannel { get; set; }

        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(256, ErrorMessage = "Validation.StringLength")]
        [Display(Name = "Field.VerificationCode")]
        public string VerificationCode { get; set; } = string.Empty;
    }

    private void RestorePrgState()
    {
        if (prgState.TryGet<HelloTotpEnrollment>(
                EnrollmentStateToken,
                out var enrollment)
            && enrollment is not null)
        {
            Enrollment = enrollment;
            PendingEnrollmentId = enrollment.EnrollmentId;
            TempData.Keep(nameof(EnrollmentStateToken));
        }

        if (prgState.TryTake<string[]>(
                RecoveryCodesStateToken,
                out var recoveryCodes)
            && recoveryCodes is not null)
        {
            RecoveryCodes = recoveryCodes;
        }
    }

    public sealed class TotpEnrollmentInput
    {
        public Guid EnrollmentId { get; set; }

        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(256, ErrorMessage = "Validation.StringLength")]
        [Display(Name = "Field.AuthenticatorCode")]
        public string Code { get; set; } = string.Empty;
    }
}
