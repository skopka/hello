using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Identity.Authentication;

namespace Skopka.Hello.UI.Pages;

public sealed class LoginModel(
    IHelloUiApplication application,
    IHelloUiExternalApplication externalApplication,
    IHelloSessionCookieManager sessionCookies,
    SkopkaHelloUiOptions uiOptions,
    HelloUiRoutePaths routes,
    IHelloUiLocalizer text,
    IHelloUiCrossDeviceApplication? crossDevice = null,
    IHelloUiWebAuthnApplication? passkeys = null)
    : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool ExternalError { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool AccountChangeRestarted { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool PasswordChangedSessionCleanup { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool AccountDeleted { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool SecurityChanged { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool RolesChanged { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool RolesChangedSessionCleanup { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool AccountSecurityChangedSessionCleanup { get; set; }

    public IReadOnlyList<Skopka.Hello.Oidc.HelloOidcProvider>
        ExternalProviders => uiOptions.IsEnabled(
            HelloUiPages.ExternalIdentity)
                ? externalApplication.Providers
                : [];

    public bool Registered { get; private set; }

    public bool PasswordReset { get; private set; }

    public bool CrossDeviceEnabled => crossDevice is not null;

    /// <summary>
    /// Offered when the host has passkeys, and hidden again by the page script
    /// on a browser that cannot do them: a button that opens nothing is worse
    /// than no button.
    /// </summary>
    public bool PasskeysEnabled => passkeys is not null;

    [BindProperty]
    public PasskeyInput Passkey { get; set; } = new();

    /// <summary>
    /// The challenge the script asks for before calling the authenticator.
    /// A POST, so the antiforgery token protects it like every other mutation
    /// here — issuing a challenge spends nothing, but answering one does.
    /// </summary>
    public async Task<IActionResult> OnPostPasskeyChallengeAsync(
        CancellationToken cancellationToken)
    {
        if (passkeys is null)
        {
            return NotFound();
        }

        var issued = await passkeys.BeginSignInAsync(
            HttpContext,
            cancellationToken);
        return issued.IsSuccess
            ? new JsonResult(issued.Value)
            : StatusCode(400);
    }

    public async Task<IActionResult> OnPostPasskeyAsync(
        CancellationToken cancellationToken)
    {
        if (passkeys is null)
        {
            return NotFound();
        }

        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, transport.Errors, text);
            return Page();
        }

        var signedIn = await passkeys.SignInAsync(
            new HelloUiWebAuthnAssertion(
                Passkey.Ticket,
                Passkey.CredentialId,
                Passkey.ClientDataJson,
                Passkey.AuthenticatorData,
                Passkey.Signature),
            HttpContext,
            cancellationToken);
        if (!signedIn.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, signedIn.Errors, text);
            return Page();
        }

        await HelloUiSession.EstablishAsync(
            HttpContext,
            sessionCookies,
            signedIn.Value);

        return Url.IsLocalUrl(ReturnUrl)
            ? LocalRedirect(ReturnUrl)
            : LocalRedirect(uiOptions.GetAuthenticatedRedirectPath(routes));
    }

    /// <summary>
    /// Filled in by the page script from what the authenticator answered. Every
    /// field is opaque text here and is checked where it is understood.
    /// </summary>
    public sealed class PasskeyInput
    {
        public string Ticket { get; set; } = string.Empty;

        public string CredentialId { get; set; } = string.Empty;

        public string ClientDataJson { get; set; } = string.Empty;

        public string AuthenticatorData { get; set; } = string.Empty;

        public string Signature { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(
        bool registered,
        bool passwordReset)
    {
        var authentication = await HttpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme);
        if (authentication.Succeeded)
        {
            return LocalRedirect(
                uiOptions.GetAuthenticatedRedirectPath(routes));
        }

        Registered = registered;
        PasswordReset = passwordReset;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                transport.Errors,
                text);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await application.LoginAsync(
            new HelloUiLoginCommand(
                Input.Login,
                Input.Password),
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

        await HelloUiSession.EstablishAsync(
            HttpContext,
            sessionCookies,
            result.Value);

        return Url.IsLocalUrl(ReturnUrl)
            ? LocalRedirect(ReturnUrl)
            : LocalRedirect(
                uiOptions.GetAuthenticatedRedirectPath(routes));
    }

    public async Task<IActionResult> OnPostExternalAsync(
        string providerId,
        CancellationToken cancellationToken)
    {
        ModelState.Clear();
        if (!uiOptions.IsEnabled(HelloUiPages.ExternalIdentity))
        {
            return NotFound();
        }

        var authentication = await HttpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme);
        if (authentication.Succeeded)
        {
            return RedirectToPage(
                "/SkopkaHello/Account/ExternalLogins");
        }

        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                transport.Errors,
                text);
            return Page();
        }

        var challenge = externalApplication.CreateSignInChallenge(
            providerId,
            ReturnUrl);
        if (!challenge.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                challenge.Errors,
                text);
            return Page();
        }

        await externalApplication.ClearBrowserFlowAsync(
            HttpContext,
            cancellationToken);

        return Challenge(
            challenge.Value.Properties,
            challenge.Value.AuthenticationScheme);
    }

    public async Task<IActionResult> OnPostCrossDeviceAsync(
        CancellationToken cancellationToken)
    {
        ModelState.Clear();
        if (crossDevice is null)
        {
            return NotFound();
        }

        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, transport.Errors, text);
            return Page();
        }

        var result = await crossDevice.BeginAsync(
            ReturnUrl,
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            return Page();
        }

        return RedirectToPage(
            "/SkopkaHello/CrossDevice/Waiting",
            new
            {
                deviceCode = result.Value.DeviceCode,
                returnUrl = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : null,
            });
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(
            IdentityLoginLimits.MaximumLoginLength,
            ErrorMessage = "Validation.StringLength")]
        [Display(Name = "Field.Login")]
        public string Login { get; set; } = string.Empty;

        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(128, ErrorMessage = "Validation.StringLength")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}
