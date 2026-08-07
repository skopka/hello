namespace Skopka.Hello.AuthorizationServer;

public sealed class HelloAuthorizationClientOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public HelloAuthorizationClientType Type { get; set; }

    public string? ClientSecret { get; set; }

    public List<string> RedirectUris { get; set; } = [];

    public List<string> Scopes { get; set; } = [];
}
