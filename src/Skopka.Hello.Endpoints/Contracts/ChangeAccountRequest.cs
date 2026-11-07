namespace Skopka.Hello.Endpoints;

public sealed record ChangeUserNameRequest(
    long ExpectedVersion,
    string UserName);

public sealed record ChangeEmailRequest(
    long ExpectedVersion,
    string? Email);

/// <summary>Legacy request retained for source compatibility. No endpoint accepts direct phone changes; use the phone-binding endpoints.</summary>
public sealed record ChangePhoneRequest(
    long ExpectedVersion,
    string? Phone);

public sealed record ReplaceProfileRequest<TProfile>(
    long ExpectedVersion,
    TProfile Profile);
