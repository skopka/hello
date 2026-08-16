namespace Skopka.Hello.Server;

public sealed record HelloProfile(
    string DisplayName,
    string? Locale)
{
    public HelloProfileRegistrationConsent? RegistrationConsent
    {
        get;
        init;
    }
}

public sealed record HelloProfileRegistrationConsent(
    bool TermsOfServiceAccepted,
    bool PrivacyPolicyAccepted,
    DateTimeOffset AcceptedAt);
