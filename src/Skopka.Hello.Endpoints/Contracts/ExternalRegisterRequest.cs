namespace Skopka.Hello.Endpoints;

public sealed record ExternalRegisterRequest<TProfile>(
    string? UserName,
    string? Email,
    string? Phone,
    TProfile Profile);
