using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Identity.Authentication;

namespace Skopka.Hello.UI.Pages;

public sealed class LoginModel(
    IHelloUiApplication application,
    IHelloUiExternalApplication externalApplication,
    IHelloSessionCookieManager sessionCookies)
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
    public bool AccountSecurityChangedSessionCleanup { get; set; }

    public IReadOnlyList<Skopka.Hello.Oidc.HelloOidcProvider>
        ExternalProviders => externalApplication.Providers;

    public bool Registered { get; private set; }

    public bool PasswordReset { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        bool registered,
        bool passwordReset)
    {
        var authentication = await HttpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme);
        if (authentication.Succeeded)
        {
            return RedirectToPage("/SkopkaHello/Account/Index");
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
                transport.Errors);
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
                result.Errors);
            return Page();
        }

        await HelloUiSession.EstablishAsync(
            HttpContext,
            sessionCookies,
            result.Value);

        return Url.IsLocalUrl(ReturnUrl)
            ? LocalRedirect(ReturnUrl)
            : RedirectToPage("/SkopkaHello/Account/Index");
    }

    public async Task<IActionResult> OnPostExternalAsync(
        string providerId,
        CancellationToken cancellationToken)
    {
        ModelState.Clear();
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
            HelloUiModelState.AddErrors(ModelState, transport.Errors);
            return Page();
        }

        var challenge = externalApplication.CreateSignInChallenge(
            providerId,
            ReturnUrl);
        if (!challenge.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                challenge.Errors);
            return Page();
        }

        await externalApplication.ClearBrowserFlowAsync(
            HttpContext,
            cancellationToken);

        return Challenge(
            challenge.Value.Properties,
            challenge.Value.AuthenticationScheme);
    }

    public sealed class InputModel
    {
        [Required]
        [StringLength(IdentityLoginLimits.MaximumLoginLength)]
        [Display(Name = "Email, phone, or user name")]
        public string Login { get; set; } = string.Empty;

        [Required]
        [StringLength(128)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}
