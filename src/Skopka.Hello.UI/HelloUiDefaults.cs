namespace Skopka.Hello.UI;

public static class HelloUiDefaults
{
    // Compatibility constants for the default prefix. Runtime links and
    // redirects use HelloUiRoutePaths from dependency injection.
    public const string AuthenticationScheme =
        "Skopka.Hello.UI";

    public const string AuthorizationPolicy =
        "Skopka.Hello.UI.Authenticated";

    public const string AccessTokenName = "access_token";

    public const string RootPath =
        Skopka.Hello.HelloUiRoutePaths.DefaultPathPrefix;

    public const string LoginPath = RootPath + "/login";

    public const string RegisterPath = RootPath + "/register";

    public const string ForgotPasswordPath =
        RootPath + "/forgot-password";

    public const string ResetPasswordPath =
        RootPath + "/reset-password";

    public const string ResendConfirmationPath =
        RootPath + "/resend-confirmation";

    public const string ResendPhoneConfirmationPath =
        RootPath + "/resend-phone-confirmation";

    public const string ConfirmEmailPath =
        RootPath + "/confirm-email";

    public const string ConfirmPhonePath =
        RootPath + "/confirm-phone";

    public const string AccountPath = RootPath + "/account";

    public const string SessionsPath =
        RootPath + "/account/sessions";

    public const string ChangePasswordPath =
        RootPath + "/account/change-password";

    public const string AccountSecurityPath =
        RootPath + "/account/security";

    public const string ExternalCompletionPath =
        RootPath + "/external/complete";

    public const string ExternalRegistrationPath =
        RootPath + "/external/register";

    public const string ExternalLoginsPath =
        RootPath + "/account/external-logins";
}
