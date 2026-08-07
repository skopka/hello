namespace Skopka.Hello.Endpoints;

public sealed record LinkedExternalProviderResponse(
    string ProviderId,
    string DisplayName,
    bool Enabled,
    bool CanUnlink,
    DateTimeOffset LinkedAt);
