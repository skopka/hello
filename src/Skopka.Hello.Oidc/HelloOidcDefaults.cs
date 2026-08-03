namespace Skopka.Hello.Oidc;

public static class HelloOidcDefaults
{
    // Compatibility constants for the default UI prefix. Runtime browser
    // redirects use HelloUiRoutePaths from dependency injection.
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
        Skopka.Hello.HelloUiRoutePaths.DefaultPathPrefix
        + "/external/complete";

    public const string RegistrationPath =
        Skopka.Hello.HelloUiRoutePaths.DefaultPathPrefix
        + "/external/register";

    public const string ExternalLoginsPath =
        Skopka.Hello.HelloUiRoutePaths.DefaultPathPrefix
        + "/account/external-logins";

    public const string FailureRedirectPath =
        Skopka.Hello.HelloUiRoutePaths.DefaultPathPrefix
        + "/login?externalError=true";
}
