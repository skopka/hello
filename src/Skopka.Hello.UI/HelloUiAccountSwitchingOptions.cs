namespace Skopka.Hello.UI;

public sealed class HelloUiAccountSwitchingOptions
{
    public bool Enabled { get; set; }

    public string CookieName { get; set; } =
        "__Host-Skopka.Hello.Accounts";

    public int MaximumSavedAccounts { get; set; } = 5;

    internal void Validate(bool secureCookies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CookieName);
        if (!secureCookies
            && CookieName.StartsWith("__Host-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "__Host- account-switching cookies must always be Secure.");
        }

        if (MaximumSavedAccounts is < 2 or > 8)
        {
            throw new InvalidOperationException(
                "MaximumSavedAccounts must be between 2 and 8.");
        }
    }
}
