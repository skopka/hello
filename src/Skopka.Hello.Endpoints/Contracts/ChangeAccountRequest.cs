namespace Skopka.Hello.Endpoints;

public sealed record ChangeUserNameRequest(
    long ExpectedVersion,
    string UserName);

public sealed record ChangeEmailRequest(
    long ExpectedVersion,
    string? Email);

public sealed record ChangePhoneRequest(
    long ExpectedVersion,
    string? Phone);

public sealed record ReplaceProfileRequest<TProfile>(
    long ExpectedVersion,
    TProfile Profile);
