namespace Skopka.Hello.AuthorizationServer;

public sealed class HelloAuthorizationClientOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public HelloAuthorizationClientType Type { get; set; }

    public string? ClientSecret { get; set; }

    public string? Resource { get; set; }

    public List<string> RedirectUris { get; set; } = [];

    public List<string> PostLogoutRedirectUris { get; set; } = [];

    public List<string> Scopes { get; set; } = [];

    /// <summary>
    /// Whether a re-authentication request from this client is obeyed. When
    /// <see langword="true"/> (the default), <c>prompt=login</c> and
    /// <c>max_age</c> sign the browser out and demand fresh credentials, which
    /// is what the standard asks for. Set it <see langword="false"/> for a
    /// trusted first-party client whose sign-in code sends one of those on
    /// every attempt regardless of intent: the request is then met with the
    /// existing single sign-on session instead of tearing it down. It has no
    /// effect on a request that carries neither, and none on any other client.
    /// </summary>
    public bool HonorReauthenticationPrompts { get; set; } = true;
}
