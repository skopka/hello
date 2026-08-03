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
    IHelloSessionCookieManager sessionCookies)
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

    public string? Changed { get; private set; }

    public bool ExternalError { get; private set; }

    public bool ChallengeRestarted { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        bool pending,
        bool externalError,
        bool challengeRestarted,
        string? changed,
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        Changed = changed is "linked" or "unlinked"
            ? changed
            : null;
        ExternalError = externalError;
        ChallengeRestarted = challengeRestarted;
        await LoadAsync(cancellationToken);
        if (pending)
        {
            await LoadPendingLinkAsync(cancellationToken);
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
            HelloUiModelState.AddErrors(ModelState, transport.Errors);
            await LoadAsync(cancellationToken);
            return Page();
        }

        var challenge = application.CreateLinkChallenge(
            providerId,
            HttpContext);
        if (!challenge.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, challenge.Errors);
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
            HelloUiModelState.AddErrors(ModelState, transport.Errors);
            await LoadAsync(cancellationToken);
            return Page();
        }

        var result = await application.BeginLinkAsync(
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors);
            await LoadAsync(cancellationToken);
            await LoadPendingLinkAsync(cancellationToken);
            return Page();
        }

        CodeRequested = true;
        CodeExpiresAt = result.Value.ExpiresAt;
        await LoadAsync(cancellationToken);
        await LoadPendingLinkAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCompleteLinkAsync(
        CancellationToken cancellationToken)
    {
        PrepareMutation("link", clearModelState: false);
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            await LoadPendingLinkAsync(cancellationToken);
            return Page();
        }

        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, transport.Errors);
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
            HelloUiModelState.AddErrors(ModelState, transport.Errors);
            return Page();
        }

        var result = await application.BeginUnlinkAsync(
            providerId,
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            PendingOperation = null;
            HelloUiModelState.AddErrors(ModelState, result.Errors);
            return Page();
        }

        CodeRequested = true;
        CodeExpiresAt = result.Value.ExpiresAt;
        return Page();
    }

    public async Task<IActionResult> OnPostCompleteUnlinkAsync(
        CancellationToken cancellationToken)
    {
        PrepareMutation("unlink", clearModelState: false);
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, transport.Errors);
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

            HelloUiModelState.AddErrors(ModelState, result.Errors);
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
            HelloUiModelState.AddErrors(ModelState, result.Errors);
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
            HelloUiModelState.AddErrors(ModelState, result.Errors);
        }
    }

    public sealed class CodeInput
    {
        [Required]
        [StringLength(8, MinimumLength = 6)]
        [Display(Name = "Verification code")]
        public string VerificationCode { get; set; } = string.Empty;
    }
}
