namespace Skopka.Hello.UI;

public enum HelloUiAccountFieldMode
{
    Hidden,
    ReadOnly,
    Editable,
}

public sealed class HelloUiAccountOptions
{
    public bool ShowUserId { get; set; } = true;

    public HelloUiAccountFieldMode UserName { get; set; } =
        HelloUiAccountFieldMode.Editable;

    public HelloUiAccountFieldMode Email { get; set; } =
        HelloUiAccountFieldMode.Editable;

    public HelloUiAccountFieldMode Phone { get; set; } =
        HelloUiAccountFieldMode.Editable;

    public static bool IsVisible(HelloUiAccountFieldMode mode)
        => mode != HelloUiAccountFieldMode.Hidden;

    public static bool IsEditable(HelloUiAccountFieldMode mode)
        => mode == HelloUiAccountFieldMode.Editable;

    public bool IsOnlyUserNameEditable
        => IsEditable(UserName)
            && !IsEditable(Email)
            && !IsEditable(Phone);

    internal void Validate()
    {
        if (!Enum.IsDefined(UserName)
            || !Enum.IsDefined(Email)
            || !Enum.IsDefined(Phone))
        {
            throw new InvalidOperationException(
                "Account fields contain an unsupported mode.");
        }
    }
}

public sealed class HelloUiContactConfirmationOptions
{
    public bool EmailEnabled { get; set; } = true;

    public bool PhoneEnabled { get; set; } = true;

    public ISet<string> TrustedEmailDomains { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool IsEmailConfirmationRequired(string? email)
    {
        if (!EmailEnabled || string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var separator = email.LastIndexOf('@');
        return separator < 0
            || separator == email.Length - 1
            || !TrustedEmailDomains.Contains(email[(separator + 1)..]);
    }

    internal void Validate()
    {
        foreach (var domain in TrustedEmailDomains)
        {
            if (string.IsNullOrWhiteSpace(domain)
                || !string.Equals(domain, domain.Trim(), StringComparison.Ordinal)
                || domain.StartsWith('@')
                || Uri.CheckHostName(domain) != UriHostNameType.Dns)
            {
                throw new InvalidOperationException(
                    "TrustedEmailDomains must contain DNS names without '@' or surrounding whitespace.");
            }
        }
    }
}
