using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Sessions;

namespace Skopka.Hello;

public sealed record HelloPhoneCodeRequest(
    Guid RequestId,
    TimeSpan RetryAfter);

public sealed record HelloPhoneCodeVerification<TProfile>(
    HelloSignIn<TProfile>? SignIn,
    string? RegistrationToken);

public sealed record HelloPhoneRegistrationCommand<TProfile>(
    string RegistrationToken,
    TProfile Profile,
    bool AcceptTermsOfService,
    bool AcceptPrivacyPolicy,
    IdentitySessionMetadata SessionMetadata);

public sealed record HelloPhonePreSendContext(
    string Phone,
    string? ClientKey,
    string? CaptchaResponse);

public interface IHelloPhonePreSendPolicy
{
    Task<OperationResult> CheckAsync(
        HelloPhonePreSendContext context,
        CancellationToken cancellationToken);
}

public interface IHelloPhoneOtpApplication<TProfile>
{
    Task<OperationResult<HelloPhoneCodeRequest>> RequestCodeAsync(
        string phone,
        string? clientKey,
        string? captchaResponse,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloPhoneCodeVerification<TProfile>>> VerifyCodeAsync(
        Guid requestId,
        string code,
        string? clientKey,
        IdentitySessionMetadata sessionMetadata,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloSignIn<TProfile>>> CompleteRegistrationAsync(
        HelloPhoneRegistrationCommand<TProfile> command,
        CancellationToken cancellationToken);
}
