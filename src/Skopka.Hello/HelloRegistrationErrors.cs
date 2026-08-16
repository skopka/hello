using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;

namespace Skopka.Hello;

public static class HelloRegistrationErrors
{
    public const string DisabledCode =
        "hello.registration.disabled";

    public const string ConsentRequiredCode =
        "hello.registration.consent_required";

    public static Error Disabled()
        => new(
            DisabledCode,
            "Self-registration is disabled.",
            ErrorType.Forbidden);

    public static Error LoginHandleRequired()
        => new(
            IdentityErrorCodes.Validation,
            "Validation failed.",
            ErrorType.Validation,
            new ValidationDetails(
                new Dictionary<string, string[]>
                {
                    ["userName"] =
                    [
                        "Enter a user name, email address or phone number.",
                    ],
                    ["email"] =
                    [
                        "Enter a user name, email address or phone number.",
                    ],
                    ["phone"] =
                    [
                        "Enter a user name, email address or phone number.",
                    ],
                }));

    public static Error ConsentRequired(
        bool termsOfService,
        bool privacyPolicy)
    {
        var fields = new Dictionary<string, string[]>();
        if (termsOfService)
        {
            fields["acceptTermsOfService"] =
            [
                "Accept the Terms of Service to create an account.",
            ];
        }

        if (privacyPolicy)
        {
            fields["acceptPrivacyPolicy"] =
            [
                "Accept the Privacy Policy to create an account.",
            ];
        }

        return new Error(
            ConsentRequiredCode,
            "Required registration consent was not provided.",
            ErrorType.Validation,
            new ValidationDetails(fields));
    }
}
