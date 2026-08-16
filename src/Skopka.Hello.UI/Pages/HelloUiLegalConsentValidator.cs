using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Skopka.Hello.UI.Pages;

internal static class HelloUiLegalConsentValidator
{
    public static void Validate(
        SkopkaHelloUiOptions options,
        ModelStateDictionary modelState,
        IHelloUiLocalizer text,
        bool acceptsTermsOfService,
        bool acceptsPrivacyPolicy)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modelState);
        ArgumentNullException.ThrowIfNull(text);

        if (options.TermsOfServiceUrl is not null
            && !acceptsTermsOfService)
        {
            modelState.AddModelError(
                "Input.AcceptTermsOfService",
                text["Validation.TermsOfServiceConsentRequired"]);
        }

        if (options.PrivacyPolicyUrl is not null
            && !acceptsPrivacyPolicy)
        {
            modelState.AddModelError(
                "Input.AcceptPrivacyPolicy",
                text["Validation.PrivacyPolicyConsentRequired"]);
        }
    }
}
