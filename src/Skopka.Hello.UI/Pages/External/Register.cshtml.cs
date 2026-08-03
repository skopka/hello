using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Identity.Authentication;
using Skopka.Hello.Oidc;

namespace Skopka.Hello.UI.Pages;

public sealed class ExternalRegisterModel(
    IHelloUiExternalApplication application,
    IHelloSessionCookieManager sessionCookies)
    : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public HelloOidcProvider? Provider { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (await IsUiAuthenticatedAsync())
        {
            return RedirectToPage(
                "/SkopkaHello/Account/ExternalLogins");
        }

        await LoadHintsAsync(prefill: true, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (await IsUiAuthenticatedAsync())
        {
            return RedirectToPage(
                "/SkopkaHello/Account/ExternalLogins");
        }

        if (!ModelState.IsValid)
        {
            await LoadHintsAsync(prefill: false, cancellationToken);
            return Page();
        }

        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, transport.Errors);
            await LoadHintsAsync(prefill: false, cancellationToken);
            return Page();
        }

        var result = await application.RegisterAsync(
            new HelloUiExternalRegisterCommand(
                Input.UserName,
                Input.Email,
                Input.Phone,
                Input.DisplayName,
                Input.Locale),
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors);
            await LoadHintsAsync(prefill: false, cancellationToken);
            return Page();
        }

        await HelloUiSession.EstablishAsync(
            HttpContext,
            sessionCookies,
            result.Value.SignIn);
        return Url.IsLocalUrl(result.Value.ReturnUrl)
            ? LocalRedirect(result.Value.ReturnUrl)
            : RedirectToPage("/SkopkaHello/Account/Index");
    }

    public async Task<IActionResult> OnPostCancelAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        await application.ClearBrowserFlowAsync(
            HttpContext,
            cancellationToken);
        return RedirectToPage("/SkopkaHello/Login");
    }

    private async Task LoadHintsAsync(
        bool prefill,
        CancellationToken cancellationToken)
    {
        var hints = await application.GetRegistrationHintsAsync(
            HttpContext,
            cancellationToken);
        if (!hints.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, hints.Errors);
            return;
        }

        Provider = hints.Value.Provider;
        if (!prefill)
        {
            return;
        }

        Input.DisplayName = hints.Value.DisplayName ?? string.Empty;
        Input.Email = hints.Value.VerifiedEmail ?? string.Empty;
        Input.Locale = hints.Value.Locale;
    }

    private async Task<bool> IsUiAuthenticatedAsync()
        => (await HttpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme)).Succeeded;

    public sealed class InputModel
    {
        [Required]
        [StringLength(200)]
        [Display(Name = "Display name")]
        public string DisplayName { get; set; } = string.Empty;

        [EmailAddress]
        [StringLength(320)]
        public string? Email { get; set; }

        [StringLength(100)]
        [Display(Name = "User name")]
        public string? UserName { get; set; }

        [StringLength(IdentityLoginLimits.MaximumLoginLength)]
        public string? Phone { get; set; }

        [StringLength(32)]
        public string? Locale { get; set; }
    }
}
