using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Skopka.Hello.UI;

internal sealed class HelloUiRazorPagesOptionsSetup(
    HelloUiRoutePaths routes,
    SkopkaHelloOptions helloOptions,
    SkopkaHelloUiOptions uiOptions,
    IEnumerable<HelloRegistrationConsentRequirement>
        consentRequirements)
    : IConfigureOptions<RazorPagesOptions>
{
    public void Configure(RazorPagesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        uiOptions.ValidateRoutes(routes);
        ValidateRegistrationConsentConfiguration();

        options.Conventions.Add(
            new HelloUiPageRouteModelConvention(
                routes,
                helloOptions.SelfRegistrationEnabled,
                uiOptions.EnabledPages));
        options.Conventions.Add(
            new HelloUiPageApplicationModelConvention());
    }

    private void ValidateRegistrationConsentConfiguration()
    {
        if (!helloOptions.SelfRegistrationEnabled
            || !uiOptions.IsEnabled(HelloUiPages.Registration))
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
