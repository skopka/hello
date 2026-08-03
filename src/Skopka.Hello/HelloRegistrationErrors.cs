using Skopka.Abstraction.OperationResult;

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
}
