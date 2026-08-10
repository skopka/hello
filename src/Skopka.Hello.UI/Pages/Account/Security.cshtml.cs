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
    IHelloUiLocalizer text)
    : PageModel
{
    public HelloCredentialState? Credentials { get; private set; }

    public PendingChallenge? Pending { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        return await LoadAsync(cancellationToken);
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
                null);
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
                null);
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
                null);
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
            Pending = new PendingChallenge(
                action,
                result.Value.ChallengeId,
                result.Value.ExpiresAt);
        }
        else
        {
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors,
                text);
        }

        var loaded = await LoadStateAsync(cancellationToken);
        return loaded ? Page() : Challenge();
    }

    private async Task<IActionResult> FinishAsync(
        Skopka.Abstraction.OperationResult.OperationResult result,
        string action,
        Guid challengeId,
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
                    null);
            }

            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors,
                text);
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
            return true;
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
        DateTimeOffset? ExpiresAt);

    public sealed class PasswordSetInput
    {
        public Guid ChallengeId { get; set; }

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

        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(256, ErrorMessage = "Validation.StringLength")]
        [Display(Name = "Field.VerificationCode")]
        public string VerificationCode { get; set; } = string.Empty;
    }
}
