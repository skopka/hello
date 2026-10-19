using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.UI;

public sealed record HelloUiPhoneVerification(
    HelloUiSignIn? SignIn,
    string? RegistrationToken);

public interface IHelloUiPhoneApplication
{
    Task<OperationResult<HelloPhoneCodeRequest>> RequestAsync(
        string phone,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloUiPhoneVerification>> VerifyAsync(
        Guid requestId,
        string code,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloUiSignIn>> RegisterAsync(
        string registrationToken,
        string displayName,
        string? locale,
        bool acceptTerms,
        bool acceptPrivacy,
        HttpContext httpContext,
        CancellationToken cancellationToken);
}

internal sealed class HelloUiPhoneApplication<TProfile>(
    IHelloPhoneOtpApplication<TProfile> application,
    IHelloUiProfileFactory<TProfile> profiles,
    IHelloRequestContext requestContext,
    IHelloRegistrationConsentPolicy consentPolicy,
    SkopkaHelloOptions options,
    TimeProvider timeProvider)
    : IHelloUiPhoneApplication
{
    public Task<OperationResult<HelloPhoneCodeRequest>> RequestAsync(
        string phone,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => application.RequestCodeAsync(
            phone,
            requestContext.CreateClientKey(httpContext),
            null,
            cancellationToken);

    public async Task<OperationResult<HelloUiPhoneVerification>> VerifyAsync(
        Guid requestId,
        string code,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var verified = await application.VerifyCodeAsync(
            requestId,
            code,
            requestContext.CreateClientKey(httpContext),
            requestContext.CreateSessionMetadata(
                httpContext, options.ClientName),
            cancellationToken);
        if (!verified.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloUiPhoneVerification>(
                verified.Errors);
        }

        return OperationResultFactory.Success(
            new HelloUiPhoneVerification(
                verified.Value.SignIn is { } signIn
                    ? ToUiSignIn(signIn)
                    : null,
                verified.Value.RegistrationToken));
    }

    public async Task<OperationResult<HelloUiSignIn>> RegisterAsync(
        string registrationToken,
        string displayName,
        string? locale,
        bool acceptTerms,
        bool acceptPrivacy,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var consent = consentPolicy.Validate(
            new HelloRegistrationConsent(
                acceptTerms,
                acceptPrivacy,
                acceptTerms || acceptPrivacy
                    ? timeProvider.GetUtcNow()
                    : null));
        if (!consent.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloUiSignIn>(consent.Errors);
        }

        var profile = profiles.Create(new HelloUiRegistrationProfile(
            displayName, locale)
        {
            RegistrationConsent = consent.Value,
        });
        if (!profile.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloUiSignIn>(
                profile.Errors);
        }

        var result = await application.CompleteRegistrationAsync(
            new HelloPhoneRegistrationCommand<TProfile>(
                registrationToken,
                profile.Value,
                acceptTerms,
                acceptPrivacy,
                requestContext.CreateSessionMetadata(
                    httpContext, options.ClientName)),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(ToUiSignIn(result.Value))
            : OperationResultFactory.Fail<HelloUiSignIn>(result.Errors);
    }

    private HelloUiSignIn ToUiSignIn(HelloSignIn<TProfile> signIn)
        => new(
            HelloUiPrincipalFactory.Create(
                signIn.Account,
                signIn.Session.SessionId,
                profiles),
            signIn.Session);
}
