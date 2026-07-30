namespace Skopka.Hello.Endpoints;

public sealed record RegisterRequest<TProfile>(
    string? UserName,
    string? Email,
    string? Phone,
    TProfile Profile,
    string Password);
