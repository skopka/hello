using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;

namespace Skopka.Hello;

public static class HelloRegistrationErrors
{
    public const string DisabledCode =
        "hello.registration.disabled";

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
}
