using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;

namespace Skopka.Hello.Oidc;

internal static class HelloOidcErrors
{
    public const string ProviderUnavailableCode =
        "hello.oidc.provider_unavailable";

    public const string PendingIdentityInvalidCode =
        "hello.oidc.pending_identity_invalid";

    public const string ProviderAlreadyLinkedCode =
        "hello.oidc.provider_already_linked";

    public const string LastSignInMethodCode =
        "hello.oidc.last_sign_in_method";

    public const string AccountRequiresLinkCode =
        "hello.oidc.account_requires_link";

    public const string ReturnUrlInvalidCode =
        "hello.oidc.return_url_invalid";

    public static Error ProviderUnavailable()
        => new(
            ProviderUnavailableCode,
            "The external sign-in provider is unavailable.",
            ErrorType.NotFound);

    public static Error PendingIdentityInvalid()
        => new(
            PendingIdentityInvalidCode,
            "The external sign-in attempt is invalid or expired.",
            ErrorType.Unauthorized);

    public static Error ProviderAlreadyLinked()
        => new(
            ProviderAlreadyLinkedCode,
            "An identity from this provider is already linked.",
            ErrorType.Conflict);

    public static Error LastSignInMethod()
        => new(
            LastSignInMethodCode,
            "The last available sign-in method cannot be removed.",
            ErrorType.Conflict);

    public static Error AccountRequiresLink()
        => new(
            AccountRequiresLinkCode,
            "Sign in to the existing account and link this provider.",
            ErrorType.Conflict);

    public static Error ReturnUrlInvalid()
        => new(
            ReturnUrlInvalidCode,
            "The external sign-in return URL must be a safe local application path.",
            ErrorType.Validation);

    public static Error AmbiguousProvider()
        => new(
            IdentityErrorCodes.ConcurrencyConflict,
            "The linked provider state is ambiguous.",
            ErrorType.Conflict);
}
