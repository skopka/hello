using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Oidc;

namespace Skopka.Hello.UI.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class ExternalLoginsModel(
    IHelloUiExternalApplication application,
    IHelloSessionCookieManager sessionCookies,
    IHelloUiLocalizer text)
    : PageModel
{
    [BindProperty]
    public CodeInput Input { get; set; } = new();

    public IReadOnlyList<HelloOidcProvider> Providers =>
        application.Providers;

    public IReadOnlyList<HelloOidcLinkedProvider> LinkedProviders
    { get; private set; } = [];

    public HelloOidcProvider? PendingProvider { get; private set; }

    public string? PendingOperation { get; private set; }

    public bool CodeRequested { get; private set; }

    public DateTimeOffset? CodeExpiresAt { get; private set; }

    public HelloDeliveryChannel? VerificationChannel { get; private set; }

    public string? Changed { get; private set; }

    public bool ExternalError { get; private set; }

    public bool ChallengeRestarted { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        bool pending,
        bool externalError,
        bool challengeRestarted,
        string? changed,
        string? pendingOperation,
        string? providerId,
        bool codeRequested,
        HelloDeliveryChannel? deliveryChannel,
        DateTimeOffset? codeExpiresAt,
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        Changed = changed is "linked" or "unlinked"
            ? changed
            : null;
        ExternalError = externalError;
        ChallengeRestarted = challengeRestarted;
        PendingOperation = pendingOperation is "link" or "unlink"
            ? pendingOperation
            : null;
        CodeRequested = codeRequested;
        if (deliveryChannel is not null
            && Enum.IsDefined(deliveryChannel.Value))
        {
            Input.DeliveryChannel = deliveryChannel.Value;
            VerificationChannel = deliveryChannel;
            CodeExpiresAt = codeExpiresAt;
        }

        await LoadAsync(cancellationToken);
        if (pending)
        {
            await LoadPendingLinkAsync(cancellationToken);
        }
        else if (PendingOperation == "unlink"
            && !string.IsNullOrWhiteSpace(providerId))
        {
            var linked = LinkedProviders.FirstOrDefault(provider =>
                string.Equals(
                    provider.ProviderId,
                    providerId,
                    StringComparison.OrdinalIgnoreCase));
            if (linked is not null)
            {
                PendingProvider = new HelloOidcProvider(
                    linked.ProviderId,
                    linked.DisplayName);
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostLinkAsync(
        string providerId,
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        ModelState.Clear();
        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, transport.Errors, text);
            await LoadAsync(cancellationToken);
            return Page();
        }

        var challenge = application.CreateLinkChallenge(
            providerId,
            HttpContext);
        if (!challenge.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, challenge.Errors, text);
            await LoadAsync(cancellationToken);
            return Page();
        }

        await application.ClearBrowserFlowAsync(
            HttpContext,
            cancellationToken);

        return Challenge(
            challenge.Value.Properties,
            challenge.Value.AuthenticationScheme);
    }

    public async Task<IActionResult> OnPostRequestLinkCodeAsync(
        CancellationToken cancellationToken)
    {
        PrepareMutation("link");
        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, transport.Errors, text);
            await LoadAsync(cancellationToken);
            return Page();
        }

        var result = await application.BeginLinkAsync(
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            await LoadAsync(cancellationToken);
            await LoadPendingLinkAsync(cancellationToken);
            return Page();
        }

        return RedirectToPage(
            "/SkopkaHello/Account/ExternalLogins",
            new
            {
                pending = true,
                pendingOperation = "link",
                codeRequested = true,
                deliveryChannel = result.Value.DeliveryChannel,
                codeExpiresAt = result.Value.ExpiresAt,
            });
    }

    public async Task<IActionResult> OnPostCompleteLinkAsync(
        CancellationToken cancellationToken)
    {
        PrepareMutation("link", clearModelState: false);
        RestoreVerificationChannel();
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            await LoadPendingLinkAsync(cancellationToken);
            return Page();
        }

        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, transport.Errors, text);
            await LoadAsync(cancellationToken);
            await LoadPendingLinkAsync(cancellationToken);
            return Page();
        }

        var result = await application.CompleteLinkAsync(
            Input.VerificationCode,
            HttpContext,
            cancellationToken);
        return await CompleteMutationAsync(
            result,
            "linked",
            loadPendingLink: true,
            cancellationToken);
    }

    public async Task<IActionResult> OnPostRequestUnlinkCodeAsync(
        string providerId,
        CancellationToken cancellationToken)
    {
        PrepareMutation("unlink");
        await LoadAsync(cancellationToken);
        var linked = LinkedProviders.FirstOrDefault(provider =>
            string.Equals(
                provider.ProviderId,
                providerId,
                StringComparison.OrdinalIgnoreCase));
        if (linked is not null)
        {
            PendingProvider = new HelloOidcProvider(
                linked.ProviderId,
                linked.DisplayName);
        }

        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            PendingOperation = null;
            HelloUiModelState.AddErrors(ModelState, transport.Errors, text);
            return Page();
        }

        var result = await application.BeginUnlinkAsync(
            providerId,
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            PendingOperation = null;
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            return Page();
        }

        return RedirectToPage(
            "/SkopkaHello/Account/ExternalLogins",
            new
            {
                pendingOperation = "unlink",
                providerId,
                codeRequested = true,
                deliveryChannel = result.Value.DeliveryChannel,
                codeExpiresAt = result.Value.ExpiresAt,
            });
    }

    public async Task<IActionResult> OnPostCompleteUnlinkAsync(
        CancellationToken cancellationToken)
    {
        PrepareMutation("unlink", clearModelState: false);
        RestoreVerificationChannel();
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, transport.Errors, text);
            await LoadAsync(cancellationToken);
            return Page();
        }

        var result = await application.CompleteUnlinkAsync(
            Input.VerificationCode,
            HttpContext,
            cancellationToken);
        return await CompleteMutationAsync(
            result,
            "unlinked",
            loadPendingLink: false,
            cancellationToken);
    }

    public async Task<IActionResult> OnPostCancelPendingAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        ModelState.Clear();
        await application.ClearBrowserFlowAsync(
            HttpContext,
            cancellationToken);
        return RedirectToPage(
            "/SkopkaHello/Account/ExternalLogins");
    }

    public bool IsLinked(string providerId)
        => LinkedProviders.Any(provider => string.Equals(
            provider.ProviderId,
            providerId,
            StringComparison.OrdinalIgnoreCase));

    private async Task<IActionResult> CompleteMutationAsync(
        OperationResult<HelloUiSignIn> result,
        string changed,
        bool loadPendingLink,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            if (result.Errors.Any(error => string.Equals(
                    error.Code,
                    HelloExternalIdentityErrorCodes
                        .ChallengeRestartRequired,
                    StringComparison.Ordinal)))
            {
                await application.ClearBrowserFlowAsync(
                    HttpContext,
                    cancellationToken);
                return RedirectToPage(
                    "/SkopkaHello/Account/ExternalLogins",
                    new { challengeRestarted = true });
            }

            if (result.Errors.Any(error => string.Equals(
                    error.Code,
                    HelloExternalIdentityErrorCodes.RestartRequired,
                    StringComparison.Ordinal)))
            {
                await application.ClearBrowserFlowAsync(
                    HttpContext,
                    cancellationToken);
                sessionCookies.DeleteSessionCookies(HttpContext);
                await HttpContext.SignOutAsync(
                    HelloUiDefaults.AuthenticationScheme);
                return RedirectToPage(
                    "/SkopkaHello/Login",
                    new { accountChangeRestarted = true });
            }

            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            CodeRequested = true;
            await LoadAsync(cancellationToken);
            if (loadPendingLink)
            {
                await LoadPendingLinkAsync(cancellationToken);
            }

            return Page();
        }

        await HelloUiSession.EstablishAsync(
            HttpContext,
            sessionCookies,
            result.Value);
        return RedirectToPage(
            "/SkopkaHello/Account/ExternalLogins",
            new { changed });
    }

    private void PrepareMutation(
        string operation,
        bool clearModelState = true)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (clearModelState)
        {
            ModelState.Clear();
        }

        PendingOperation = operation;
    }

    private void RestoreVerificationChannel()
        => VerificationChannel = Enum.IsDefined(Input.DeliveryChannel)
            ? Input.DeliveryChannel
            : null;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var result = await application.ListLinkedProvidersAsync(
            HttpContext,
            cancellationToken);
        if (result.IsSuccess)
        {
            LinkedProviders = result.Value;
        }
        else
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
        }
    }

    private async Task LoadPendingLinkAsync(
        CancellationToken cancellationToken)
    {
        var result = await application.GetPendingLinkAsync(
            HttpContext,
            cancellationToken);
        if (result.IsSuccess)
        {
            PendingProvider = result.Value;
            PendingOperation = "link";
        }
        else
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
        }
    }

    public sealed class CodeInput
    {
        public HelloDeliveryChannel DeliveryChannel { get; set; }

        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(
            64,
            MinimumLength = 6,
            ErrorMessage = "Validation.StringLengthRange")]
        [Display(Name = "Field.VerificationCode")]
        public string VerificationCode { get; set; } = string.Empty;
    }
}
