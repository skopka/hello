using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Identity.Authentication;
using Skopka.Hello.Oidc;

namespace Skopka.Hello.UI.Pages;

[method: ActivatorUtilitiesConstructor]
public sealed class ExternalRegisterModel(
    IHelloUiExternalApplication application,
    IHelloSessionCookieManager sessionCookies,
    IHelloUiLocalizer text,
    SkopkaHelloUiOptions uiOptions)
    : PageModel
{
    public ExternalRegisterModel(
        IHelloUiExternalApplication application,
        IHelloSessionCookieManager sessionCookies,
        IHelloUiLocalizer text)
        : this(
            application,
            sessionCookies,
            text,
            new SkopkaHelloUiOptions())
    {
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public HelloUiRegistrationOptions Registration =>
        uiOptions.Registration;

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

        var values = HelloUiRegistrationFormValidator.Validate(
            Registration,
            ModelState,
            text,
            Input.DisplayName,
            Input.Email,
            Input.UserName,
            Input.Phone,
            Input.Locale,
            requireLoginIdentifier: false);
        Input.DisplayName = values.DisplayName;
        Input.Email = values.Email;
        Input.UserName = values.UserName;
        Input.Phone = values.Phone;
        Input.Locale = values.Locale;
        if (!ModelState.IsValid)
        {
            await LoadHintsAsync(prefill: false, cancellationToken);
            return Page();
        }

        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                transport.Errors,
                text);
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
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors,
                text);
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
            HelloUiModelState.AddErrors(
                ModelState,
                hints.Errors,
                text);
            return;
        }

        Provider = hints.Value.Provider;
        if (!prefill)
        {
            return;
        }

        if (Registration.IsVisible(
                HelloUiRegistrationField.DisplayName))
        {
            Input.DisplayName = hints.Value.DisplayName ?? string.Empty;
        }

        if (Registration.IsVisible(HelloUiRegistrationField.Email))
        {
            Input.Email = hints.Value.VerifiedEmail ?? string.Empty;
        }

        if (Registration.IsVisible(HelloUiRegistrationField.Locale))
        {
            Input.Locale = hints.Value.Locale;
        }
    }

    private async Task<bool> IsUiAuthenticatedAsync()
        => (await HttpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme)).Succeeded;

    public sealed class InputModel
    {
        [StringLength(200, ErrorMessage = "Validation.StringLength")]
        [Display(Name = "Field.DisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Validation.EmailAddress")]
        [StringLength(320, ErrorMessage = "Validation.StringLength")]
        public string? Email { get; set; }

        [StringLength(100, ErrorMessage = "Validation.StringLength")]
        [Display(Name = "Field.UserName")]
        public string? UserName { get; set; }

        [StringLength(
            IdentityLoginLimits.MaximumLoginLength,
            ErrorMessage = "Validation.StringLength")]
        public string? Phone { get; set; }

        [StringLength(32, ErrorMessage = "Validation.StringLength")]
        public string? Locale { get; set; }
    }
}
