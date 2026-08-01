namespace Skopka.Hello.Endpoints;

public sealed record LinkedExternalProviderResponse(
    string ProviderId,
    string DisplayName,
    bool Enabled,
    DateTimeOffset LinkedAt);
