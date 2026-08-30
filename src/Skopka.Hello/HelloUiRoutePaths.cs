namespace Skopka.Hello;

/// <summary>
/// Contains the UI routes derived from the configured UI path prefix.
/// </summary>
public sealed class HelloUiRoutePaths
{
    public const string DefaultPathPrefix = "/hello";

    internal HelloUiRoutePaths(string pathPrefix)
    {
        PathPrefix = NormalizePathPrefix(pathPrefix);
        RootPath = PathPrefix;
        LoginPath = Append("/login");
        RegisterPath = Append("/register");
        ForgotPasswordPath = Append("/forgot-password");
        ResetPasswordPath = Append("/reset-password");
        ResendConfirmationPath = Append("/resend-confirmation");
        ResendPhoneConfirmationPath =
            Append("/resend-phone-confirmation");
        ConfirmEmailPath = Append("/confirm-email");
        ConfirmPhonePath = Append("/confirm-phone");
        AccountPath = Append("/account");
        SessionsPath = Append("/account/sessions");
        ChangePasswordPath = Append("/account/change-password");
        AccountSecurityPath = Append("/account/security");
        ExternalCompletionPath = Append("/external/complete");
        ExternalRegistrationPath = Append("/external/register");
        ExternalLoginsPath = Append("/account/external-logins");
        CrossDeviceWaitingPath = Append("/cross-device");
        CrossDeviceApprovalPath = Append("/cross-device/approve");
        CulturePath = Append("/culture");
        ErrorPath = Append("/error");
    }

    public string PathPrefix { get; }

    public string RootPath { get; }

    public string LoginPath { get; }

    public string RegisterPath { get; }

    public string ForgotPasswordPath { get; }

    public string ResetPasswordPath { get; }

    public string ResendConfirmationPath { get; }

    public string ResendPhoneConfirmationPath { get; }

    public string ConfirmEmailPath { get; }

    public string ConfirmPhonePath { get; }

    public string AccountPath { get; }

    public string SessionsPath { get; }

    public string ChangePasswordPath { get; }

    public string AccountSecurityPath { get; }

    public string ExternalCompletionPath { get; }

    public string ExternalRegistrationPath { get; }

    public string ExternalLoginsPath { get; }

    public string CrossDeviceWaitingPath { get; }

    public string CrossDeviceApprovalPath { get; }

    public string CulturePath { get; }

    public string ErrorPath { get; }

    internal static void ValidatePathPrefix(string? pathPrefix)
        => _ = NormalizePathPrefix(pathPrefix);

    private static string NormalizePathPrefix(string? pathPrefix)
    {
        if (string.IsNullOrEmpty(pathPrefix)
            || pathPrefix == "/")
        {
            throw new InvalidOperationException(
                "UiPathPrefix must be a non-empty absolute local path other than '/'.");
        }

        if (pathPrefix.Length > 256
            || !pathPrefix.StartsWith('/')
            || pathPrefix.EndsWith('/')
            || pathPrefix.Contains("//", StringComparison.Ordinal)
            || pathPrefix.Contains("/./", StringComparison.Ordinal)
            || pathPrefix.Contains("/../", StringComparison.Ordinal)
            || pathPrefix.EndsWith("/.", StringComparison.Ordinal)
            || pathPrefix.EndsWith("/..", StringComparison.Ordinal)
            || UsesReservedNamespace(pathPrefix)
            || pathPrefix.IndexOfAny(
                ['?', '#', '\\', '{', '}', '*', '%']) >= 0
            || pathPrefix.Any(character =>
                char.IsWhiteSpace(character)
                || char.IsControl(character)))
        {
            throw new InvalidOperationException(
                "UiPathPrefix must be a non-empty absolute local path other than '/', of at most 256 characters, without a trailing slash, empty or dot segments, route parameters, escaping, a query or a fragment. The '/auth', '/account', '/health', '/swagger', '/openapi', '/_content' and '/signin-skopka-oidc' namespaces are reserved.");
        }

        return pathPrefix;
    }

    private string Append(string suffix)
        => PathPrefix + suffix;

    private static bool UsesReservedNamespace(string pathPrefix)
    {
        var separator = pathPrefix.IndexOf('/', 1);
        var firstSegment = separator < 0
            ? pathPrefix
            : pathPrefix[..separator];
        return firstSegment.Equals(
                "/auth",
                StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals(
                "/account",
                StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals(
                "/health",
                StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals(
                "/swagger",
                StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals(
                "/openapi",
                StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals(
                "/_content",
                StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals(
                "/signin-skopka-oidc",
                StringComparison.OrdinalIgnoreCase);
    }
}
