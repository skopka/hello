using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.UI;

public sealed record HelloUiPhoneVerification(
    HelloUiSignIn? SignIn,
    string? RegistrationToken);

public interface IHelloUiPhoneApplication
{
    IReadOnlyList<HelloUiProfileField> RegistrationFields => [];

    Task<OperationResult<HelloUiSignIn>> RegisterWithValuesAsync(
        string registrationToken, string displayName, string? locale, bool acceptTerms, bool acceptPrivacy,
        IReadOnlyDictionary<string, string?> values, HttpContext httpContext, CancellationToken cancellationToken)
        => RegisterAsync(registrationToken, displayName, locale, acceptTerms, acceptPrivacy, httpContext, cancellationToken);

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
    TimeProvider timeProvider,
    IHelloUiRegistrationFields? registrationFields = null)
    : IHelloUiPhoneApplication
{
    public IReadOnlyList<HelloUiProfileField> RegistrationFields => registrationFields?.GetFields() ?? [];

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
        => await RegisterWithValuesAsync(registrationToken, displayName, locale, acceptTerms, acceptPrivacy,
            new Dictionary<string, string?>(), httpContext, cancellationToken);

    public async Task<OperationResult<HelloUiSignIn>> RegisterWithValuesAsync(
        string registrationToken, string displayName, string? locale, bool acceptTerms, bool acceptPrivacy,
        IReadOnlyDictionary<string, string?> values, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var fields = RegistrationFields;
        var validation = HelloUiRegistrationFieldsValidator.Validate(fields, values);
        if (!validation.IsSuccess)
            return OperationResultFactory.Fail<HelloUiSignIn>(validation.Errors);
        var checkedValues = registrationFields?.Validate(values);
        if (checkedValues is not null && !checkedValues.IsSuccess)
            return OperationResultFactory.Fail<HelloUiSignIn>(checkedValues.Errors);
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
            Values = checkedValues?.Value ?? values,
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
