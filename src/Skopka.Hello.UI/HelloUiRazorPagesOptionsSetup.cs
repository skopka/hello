using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Skopka.Hello.UI;

internal sealed class HelloUiRazorPagesOptionsSetup(
    HelloUiRoutePaths routes,
    SkopkaHelloOptions helloOptions,
    HelloCrossDeviceSignInOptions crossDeviceOptions,
    SkopkaHelloUiOptions uiOptions,
    IServiceProvider services,
    IEnumerable<HelloRegistrationConsentRequirement>
        consentRequirements)
    : IConfigureOptions<RazorPagesOptions>
{
    public void Configure(RazorPagesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        uiOptions.ValidateRoutes(routes);
        ValidateRegistrationConsentConfiguration();
        if (helloOptions.Sites.Count != 0 && !uiOptions.SecureCookies)
            throw new InvalidOperationException("Explicit public sites require secure UI cookies.");
        foreach (var site in helloOptions.Sites)
        {
            if (site.EnabledFeatures.HasFlag(HelloSiteFeatures.Login)
                && !site.EnabledFeatures.HasFlag(HelloSiteFeatures.Account)
                && site.AuthenticatedRedirectPath is null && uiOptions.AuthenticatedRedirectPath is null)
                throw new InvalidOperationException("A login-only site requires an authenticated redirect path.");
            if (string.Equals(site.AuthenticatedRedirectPath, routes.LoginPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A site's authenticated redirect cannot point to the Login page.");
        }
        if (services.GetService<HelloNativeAppSignInOptions>()?.Enabled == true
            && (helloOptions.Sites.Count == 0 && (helloOptions.PublicOrigin is null || helloOptions.PublicOrigin.Scheme != Uri.UriSchemeHttps)
                || !helloOptions.SecureCookies || !uiOptions.SecureCookies))
        {
            throw new InvalidOperationException("Native app sign-in requires an HTTPS PublicOrigin and secure session cookies.");
        }
        if (services.GetService<HelloNativeAppSignInOptions>()?.Enabled == true
            && !uiOptions.IsEnabled(HelloUiPages.Login))
        {
            throw new InvalidOperationException("Native app sign-in requires the Login page.");
        }

        options.Conventions.Add(
            new HelloUiPageRouteModelConvention(
                routes,
                helloOptions.SelfRegistrationEnabled,
                helloOptions.PasswordSignInEnabled,
                crossDeviceOptions.Enabled,
                uiOptions.AccountSwitching.Enabled,
                uiOptions.EnabledPages,
                uiOptions.ContactConfirmation.EmailEnabled,
                uiOptions.ContactConfirmation.PhoneEnabled,
                services.GetService<HelloNativeAppSignInOptions>()?.Enabled == true,
                services.GetService<IServiceProviderIsService>()?.IsService(typeof(IHelloUiAccessLinks)) == true));
        options.Conventions.Add(
            new HelloUiPageApplicationModelConvention());
    }

    private void ValidateRegistrationConsentConfiguration()
    {
        if (!helloOptions.SelfRegistrationEnabled
            || !(helloOptions.PhoneOtpEnabled
                && uiOptions.IsEnabled(HelloUiPages.Login))
                && !(helloOptions.PasswordSignInEnabled
                    && uiOptions.IsEnabled(HelloUiPages.Registration)))
        {
            return;
        }

        var termsRequired =
            helloOptions.RegistrationConsent.TermsOfServiceRequired
            || consentRequirements.Any(requirement =>
                requirement.TermsOfServiceRequired);
        var privacyRequired =
            helloOptions.RegistrationConsent.PrivacyPolicyRequired
            || consentRequirements.Any(requirement =>
                requirement.PrivacyPolicyRequired);

        if (termsRequired && uiOptions.TermsOfServiceUrl is null)
        {
            throw new InvalidOperationException(
                "TermsOfServiceUrl is required when the Terms of Service consent policy applies to the registration UI.");
        }

        if (privacyRequired && uiOptions.PrivacyPolicyUrl is null)
        {
            throw new InvalidOperationException(
                "PrivacyPolicyUrl is required when the Privacy Policy consent policy applies to the registration UI.");
        }
    }
}
