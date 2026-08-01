namespace Skopka.Hello.Oidc;

public static class HelloOidcDefaults
{
    public const string ExternalCookieScheme =
        "Skopka.Hello.Oidc.External";

    public const string ExternalCookieName =
        "__Host-Skopka.Hello.External";

    public const string PendingCookieScheme =
        "Skopka.Hello.Oidc.Pending";

    public const string PendingCookieName =
        "__Host-Skopka.Hello.External.Pending";

    public const string ProviderSchemePrefix =
        "Skopka.Hello.Oidc.Provider.";

    public const string CallbackPathPrefix =
        "/signin-skopka-oidc/";

    public const string CompletionPath =
        "/hello/external/complete";

    public const string RegistrationPath =
        "/hello/external/register";

    public const string ExternalLoginsPath =
        "/hello/account/external-logins";

    public const string FailureRedirectPath =
        "/hello/login?externalError=true";
}
