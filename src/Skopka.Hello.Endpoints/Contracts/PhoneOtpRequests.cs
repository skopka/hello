namespace Skopka.Hello.Endpoints;

public sealed record PhoneCodeRequest(string Phone, string? CaptchaResponse = null);

public sealed record PhoneCodeVerifyRequest(Guid RequestId, string Code);

public sealed record PhoneRegistrationRequest<TProfile>(
    string RegistrationToken,
    TProfile Profile,
    bool AcceptTermsOfService,
    bool AcceptPrivacyPolicy);

public sealed record PhoneCodeRequestResponse(
    Guid RequestId,
    int RetryAfterSeconds);

public sealed record PhoneCodeVerifyResponse(
    string Status,
    SessionResponse? Session,
    string? RegistrationToken);
