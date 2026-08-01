namespace Skopka.Hello.Oidc;

public sealed record HelloOidcProvider(
    string Id,
    string DisplayName);

public interface IHelloOidcProviderCatalog
{
    IReadOnlyList<HelloOidcProvider> Providers { get; }

    bool IsEnabled(string providerId);
}

internal sealed class HelloOidcProviderCatalog(
    IReadOnlyList<HelloOidcProviderRegistration> registrations)
    : IHelloOidcProviderCatalog
{
    private readonly Dictionary<string, HelloOidcProviderRegistration>
        registrationsById = registrations.ToDictionary(
            provider => provider.Id,
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<HelloOidcProvider> Providers { get; } =
        registrations
            .Select(provider => new HelloOidcProvider(
                provider.Id,
                provider.DisplayName))
            .ToArray();

    public bool IsEnabled(string providerId)
        => !string.IsNullOrWhiteSpace(providerId)
            && registrationsById.ContainsKey(providerId);

    public bool TryGet(
        string? providerId,
        out HelloOidcProviderRegistration registration)
        => registrationsById.TryGetValue(
            providerId ?? string.Empty,
            out registration!);
}
