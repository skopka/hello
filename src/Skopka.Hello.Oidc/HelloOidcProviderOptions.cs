namespace Skopka.Hello.Oidc;

public sealed class HelloOidcProviderOptions
{
    public bool Enabled { get; set; } = true;

    public string DisplayName { get; set; } = string.Empty;

    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public bool RequireHttpsMetadata { get; set; } = true;

    public int Order { get; set; }

    public IList<string> Scopes { get; } = new List<string>();
}
