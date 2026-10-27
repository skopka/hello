using System.Globalization;
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
    IHelloUiWebAuthnApplication? passkeys = null,
    IHelloUiPhoneApplication? phoneApplication = null,
    SkopkaHelloOptions? helloOptions = null)
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

    public bool PhoneEnabled => phoneApplication is not null;

    public bool PasswordEnabled => helloOptions?.PasswordSignInEnabled ?? true;

    [BindProperty]
    public PhoneInput Phone { get; set; } = new();

    public sealed class PhoneInput
    {
        [Required(ErrorMessage = "Validation.Required")]
        [Display(Name = "Field.PhoneNumber")]
        public string PhoneNumber { get; set; } = string.Empty;
        public Guid RequestId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string RegistrationToken { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Locale { get; set; }
        public bool AcceptTermsOfService { get; set; }
        public bool AcceptPrivacyPolicy { get; set; }
        public int RetryAfterSeconds { get; set; }
    }

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
        foreach (var key in ModelState.Keys
            .Where(key => key.Equals("Phone", StringComparison.OrdinalIgnoreCase)
                || key.Equals(nameof(PhoneInput.PhoneNumber), StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("Phone.", StringComparison.OrdinalIgnoreCase))
            .ToArray())
        {
            ModelState.Remove(key);
        }

        if (!PasswordEnabled)
        {
            return NotFound();
        }

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

    public async Task<IActionResult> OnPostPhoneRequestAsync(
        CancellationToken cancellationToken)
    {
        ModelState.Clear();
        if (phoneApplication is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(Phone.PhoneNumber))
        {
            ModelState.AddModelError("Phone.PhoneNumber",
                text["Validation.Required", text["Field.PhoneNumber"].Value].Value);
            return Page();
        }
        ModelState.SetModelValue("Phone.PhoneNumber", Phone.PhoneNumber, Phone.PhoneNumber);
        var result = await phoneApplication.RequestAsync(
            Phone.PhoneNumber, HttpContext, cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text,
                field => string.Equals(field, "phone", StringComparison.OrdinalIgnoreCase)
                    ? "Phone.PhoneNumber" : $"Phone.{field}");
            return Page();
        }

        Phone.RequestId = result.Value.RequestId;
        Phone.RetryAfterSeconds = (int)Math.Ceiling(
            result.Value.RetryAfter.TotalSeconds);
        return Page();
    }

    public async Task<IActionResult> OnPostPhoneVerifyAsync(
        CancellationToken cancellationToken)
    {
        ModelState.Clear();
        if (phoneApplication is null)
        {
            return NotFound();
        }

        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, transport.Errors, text);
            return Page();
        }

        var result = await phoneApplication.VerifyAsync(
            Phone.RequestId, Phone.Code, HttpContext, cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            return Page();
        }

        if (result.Value.SignIn is { } signIn)
        {
            await HelloUiSession.EstablishAsync(
                HttpContext, sessionCookies, signIn);
            return Url.IsLocalUrl(ReturnUrl)
                ? LocalRedirect(ReturnUrl)
                : LocalRedirect(
                    uiOptions.GetAuthenticatedRedirectPath(routes));
        }

        Phone.RegistrationToken = result.Value.RegistrationToken ?? string.Empty;
        return Page();
    }

    public async Task<IActionResult> OnPostPhoneRegisterAsync(
        CancellationToken cancellationToken)
    {
        ModelState.Clear();
        if (phoneApplication is null)
        {
            return NotFound();
        }

        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, transport.Errors, text);
            return Page();
        }

        if (!uiOptions.Registration.IsVisible(HelloUiRegistrationField.Locale))
        {
            Phone.Locale = CultureInfo.CurrentCulture.Name;
        }
        else if (uiOptions.Registration.IsRequired(HelloUiRegistrationField.Locale)
            && string.IsNullOrWhiteSpace(Phone.Locale))
        {
            ModelState.AddModelError("Phone.Locale",
                text["Validation.Required", text["Field.Locale"].Value].Value);
            return Page();
        }

        ModelState.SetModelValue("Phone.AcceptTermsOfService", Phone.AcceptTermsOfService.ToString(), Phone.AcceptTermsOfService.ToString());
        ModelState.SetModelValue("Phone.AcceptPrivacyPolicy", Phone.AcceptPrivacyPolicy.ToString(), Phone.AcceptPrivacyPolicy.ToString());
        ModelState.SetModelValue("Phone.Locale", Phone.Locale, Phone.Locale);
        var result = await phoneApplication.RegisterAsync(
            Phone.RegistrationToken,
            Phone.DisplayName,
            Phone.Locale,
            Phone.AcceptTermsOfService,
            Phone.AcceptPrivacyPolicy,
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text,
                field => $"Phone.{field}");
            return Page();
        }

        await HelloUiSession.EstablishAsync(
            HttpContext, sessionCookies, result.Value);
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
