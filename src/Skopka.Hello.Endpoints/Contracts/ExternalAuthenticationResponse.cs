using System.Text.Json.Serialization;

namespace Skopka.Hello.Endpoints;

[JsonConverter(typeof(JsonStringEnumConverter<ExternalAuthenticationOutcome>))]
public enum ExternalAuthenticationOutcome
{
    SignedIn = 1,
    RegistrationRequired = 2,
    LinkVerificationRequired = 3,
}

public sealed record ExternalRegistrationHintsResponse(
    ExternalProviderResponse Provider,
    string? DisplayName,
    string? VerifiedEmail,
    string? Locale);

public sealed record ExternalAuthenticationResponse(
    ExternalAuthenticationOutcome Outcome,
    SessionResponse? Session,
    ExternalRegistrationHintsResponse? Registration,
    ExternalProviderResponse? Provider,
    string ReturnUrl);
