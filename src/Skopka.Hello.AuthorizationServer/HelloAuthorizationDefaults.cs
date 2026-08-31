namespace Skopka.Hello.AuthorizationServer;

public static class HelloAuthorizationDefaults
{
    public const string AuthorizationEndpointPath = "/connect/authorize";
    public const string TokenEndpointPath = "/connect/token";
    public const string EndSessionEndpointPath = "/connect/logout";
    public const string RolesScope = "roles";
    public const string CompositeBearerAuthenticationScheme =
        "Skopka.Hello.Bearer";
    public const string OAuthAuthenticationScheme =
        "Skopka.Hello.OAuth";
    public const string OAuthTransportClaim = "skopka_token_transport";
    public const string OAuthTransport = "oauth";

    internal const string SourceSessionIdClaim =
        "skopka_source_session_id";
}
