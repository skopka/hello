namespace Skopka.Hello.Endpoints;

public sealed record ExternalProviderResponse(
    string ProviderId,
    string DisplayName);
