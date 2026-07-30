using Skopka.Identity.Users;

namespace Skopka.Hello.Endpoints;

public sealed record AccountResponse<TProfile>(
    Guid Id,
    UserFlags Flags,
    string? UserName,
    string? Email,
    bool EmailConfirmed,
    string? Phone,
    bool PhoneConfirmed,
    TProfile Profile,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt);
