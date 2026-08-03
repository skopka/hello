namespace Skopka.Hello.Oidc;

internal static class HelloOidcProperties
{
    public const string Intent = "hello:oidc:intent";
    public const string Provider = "hello:oidc:provider";
    public const string ReturnUrl = "hello:oidc:return_url";
    public const string UserId = "hello:oidc:user_id";
    public const string SessionId = "hello:oidc:session_id";
    public const string ChallengeId = "hello:oidc:challenge_id";
    public const string FlowId = "hello:oidc:flow_id";

    public const string SignInIntent = "sign_in";
    public const string LinkIntent = "link";
    public const string UnlinkIntent = "unlink";
}
