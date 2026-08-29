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
    IHelloUiCrossDeviceApplication? crossDevice = null)
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
