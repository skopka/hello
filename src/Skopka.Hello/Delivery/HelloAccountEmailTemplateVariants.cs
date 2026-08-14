namespace Skopka.Hello;

public static class HelloAccountEmailTemplateVariants
{
    public const string PasswordSet = "account.password.set";
    public const string PasswordRemove = "account.password.remove";
    public const string AccountDelete = "account.delete";
    public const string AuthenticatorDisable =
        "account.authenticator.disable";

    internal static IReadOnlyList<string> AccountSecurityVariants { get; } =
    [
        PasswordSet,
        PasswordRemove,
        AccountDelete,
        AuthenticatorDisable,
    ];
}
