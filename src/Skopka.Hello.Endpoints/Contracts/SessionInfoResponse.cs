namespace Skopka.Hello.Endpoints;

public sealed record SessionInfoResponse(
    Guid SessionId,
    string? ClientName,
    string? DeviceName,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastRefreshedAt);
