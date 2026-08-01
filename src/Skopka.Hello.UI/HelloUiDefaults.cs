namespace Skopka.Hello.UI;

public static class HelloUiDefaults
{
    public const string AuthenticationScheme =
        "Skopka.Hello.UI";

    public const string AuthorizationPolicy =
        "Skopka.Hello.UI.Authenticated";

    public const string AccessTokenName = "access_token";

    public const string RootPath = "/hello";

    public const string LoginPath = "/hello/login";

    public const string RegisterPath = "/hello/register";

    public const string ForgotPasswordPath =
        "/hello/forgot-password";

    public const string ResetPasswordPath =
        "/hello/reset-password";

    public const string ResendConfirmationPath =
        "/hello/resend-confirmation";

    public const string ConfirmEmailPath =
        "/hello/confirm-email";

    public const string AccountPath = "/hello/account";

    public const string SessionsPath = "/hello/account/sessions";

    public const string ChangePasswordPath =
        "/hello/account/change-password";

    public const string ExternalCompletionPath =
        "/hello/external/complete";

    public const string ExternalRegistrationPath =
        "/hello/external/register";

    public const string ExternalLoginsPath =
        "/hello/account/external-logins";
}
