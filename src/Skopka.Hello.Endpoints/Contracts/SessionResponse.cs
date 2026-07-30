namespace Skopka.Hello.Endpoints;

public sealed record SessionResponse(
    Guid SessionId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt);
