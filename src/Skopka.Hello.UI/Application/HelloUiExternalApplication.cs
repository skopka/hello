using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Oidc;
using Skopka.Identity.Errors;

namespace Skopka.Hello.UI;

internal sealed class HelloUiExternalApplication<TProfile>(
    IEnumerable<IHelloOidcApplication<TProfile>> applications,
    IEnumerable<IHelloOidcChallengeService> challengeServices,
    IEnumerable<IHelloOidcProviderCatalog> providerCatalogs,
    IHelloUiProfileFactory<TProfile> profiles,
    IHelloRequestContext requestContext,
    SkopkaHelloOptions helloOptions)
    : IHelloUiExternalApplication
{
    private readonly IHelloOidcApplication<TProfile>? oidc =
        applications.SingleOrDefault();

    private readonly IHelloOidcChallengeService? challenges =
        challengeServices.SingleOrDefault();

    private readonly IHelloOidcProviderCatalog? catalog =
        providerCatalogs.SingleOrDefault();

    public bool IsConfigured => oidc is not null && catalog is not null;

    public IReadOnlyList<HelloOidcProvider> Providers =>
        catalog?.Providers ?? [];

    public OperationResult<HelloOidcChallenge> CreateSignInChallenge(
        string providerId,
        string? returnUrl)
        => challenges is null
            ? Unavailable<HelloOidcChallenge>()
            : challenges.CreateSignIn(providerId, returnUrl);

    public OperationResult<HelloOidcChallenge> CreateLinkChallenge(
        string providerId,
        HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (challenges is null)
        {
            return Unavailable<HelloOidcChallenge>();
        }

        return HelloUiPrincipalFactory.TryGetUserId(
                httpContext.User,
                out var userId)
            && HelloUiPrincipalFactory.TryGetSessionId(
                httpContext.User,
                out var sessionId)
            ? challenges.CreateLink(providerId, userId, sessionId)
            : InvalidSession<HelloOidcChallenge>();
    }

    public async Task<OperationResult<HelloUiExternalCompletion>>
        CompleteChallengeAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (oidc is null)
        {
            return Unavailable<HelloUiExternalCompletion>();
        }

        var local = await TryReadLocalSessionAsync(httpContext);
        var result = await oidc.CompleteChallengeAsync(
            httpContext,
            local,
            CreateSessionMetadata(httpContext),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return OperationResultFactory.Fail<
                HelloUiExternalCompletion>(result.Errors);
        }

        return OperationResultFactory.Success(
            new HelloUiExternalCompletion(
                result.Value.Kind,
                result.Value.SignIn is null
                    ? null
                    : MapSignIn(result.Value.SignIn),
                result.Value.Registration,
                result.Value.Provider,
                result.Value.ReturnUrl));
    }

    public Task<OperationResult<HelloOidcRegistrationHints>>
        GetRegistrationHintsAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
        => oidc is null
            ? Task.FromResult(
                Unavailable<HelloOidcRegistrationHints>())
            : oidc.GetRegistrationHintsAsync(
                httpContext,
                cancellationToken);

    public async Task<OperationResult<HelloUiExternalRegistration>>
        RegisterAsync(
            HelloUiExternalRegisterCommand command,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(httpContext);
        if (!helloOptions.SelfRegistrationEnabled)
        {
            return OperationResultFactory.Fail<
                HelloUiExternalRegistration>(
                    HelloRegistrationErrors.Disabled());
        }

        if (oidc is null)
        {
            return Unavailable<HelloUiExternalRegistration>();
        }

        var profile = profiles.Create(
            new HelloUiRegistrationProfile(
                command.DisplayName,
                command.Locale));
        if (!profile.IsSuccess)
        {
            return OperationResultFactory.Fail<
                HelloUiExternalRegistration>(profile.Errors);
        }

        var hints = await oidc.GetRegistrationHintsAsync(
            httpContext,
            cancellationToken);
        if (!hints.IsSuccess)
        {
            return OperationResultFactory.Fail<
                HelloUiExternalRegistration>(hints.Errors);
        }

        var result = await oidc.RegisterAsync(
            new HelloOidcRegisterCommand<TProfile>(
                command.UserName,
                command.Email,
                command.Phone,
                profile.Value,
                CreateSessionMetadata(httpContext)),
            httpContext,
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(
                new HelloUiExternalRegistration(
                    MapSignIn(result.Value),
                    hints.Value.ReturnUrl))
            : OperationResultFactory.Fail<
                HelloUiExternalRegistration>(result.Errors);
    }

    public async Task<OperationResult<
        IReadOnlyList<HelloOidcLinkedProvider>>>
        ListLinkedProvidersAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var state = await RequireStateAsync(httpContext);
        if (!state.IsSuccess)
        {
            return OperationResultFactory.Fail<
                IReadOnlyList<HelloOidcLinkedProvider>>(state.Errors);
        }

        return await oidc!.ListLinkedProvidersAsync(
            state.Value.AccessToken,
            cancellationToken);
    }

    public async Task<OperationResult<HelloOidcProvider>>
        GetPendingLinkAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var state = await RequireStateAsync(httpContext);
        return state.IsSuccess
            ? await oidc!.GetPendingLinkAsync(
                httpContext,
                state.Value,
                cancellationToken)
            : OperationResultFactory.Fail<HelloOidcProvider>(
                state.Errors);
    }

    public async Task<OperationResult<HelloStepUpChallenge>> BeginLinkAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var state = await RequireStateAsync(httpContext);
        return state.IsSuccess
            ? await oidc!.BeginLinkAsync(
                httpContext,
                state.Value,
                requestContext.CreateClientKey(httpContext),
                cancellationToken)
            : OperationResultFactory.Fail<HelloStepUpChallenge>(
                state.Errors);
    }

    public Task<OperationResult<HelloUiSignIn>> CompleteLinkAsync(
        string verificationCode,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => CompleteMutationAsync(
            verificationCode,
            httpContext,
            link: true,
            cancellationToken);

    public async Task<OperationResult<HelloStepUpChallenge>>
        BeginUnlinkAsync(
            string providerId,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var state = await RequireStateAsync(httpContext);
        return state.IsSuccess
            ? await oidc!.BeginUnlinkAsync(
                providerId,
                httpContext,
                state.Value,
                requestContext.CreateClientKey(httpContext),
                cancellationToken)
            : OperationResultFactory.Fail<HelloStepUpChallenge>(
                state.Errors);
    }

    public Task<OperationResult<HelloUiSignIn>> CompleteUnlinkAsync(
        string verificationCode,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => CompleteMutationAsync(
            verificationCode,
            httpContext,
            link: false,
            cancellationToken);

    public Task ClearBrowserFlowAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => oidc?.ClearBrowserFlowAsync(httpContext, cancellationToken)
            ?? Task.CompletedTask;

    private async Task<OperationResult<HelloUiSignIn>>
        CompleteMutationAsync(
            string verificationCode,
            HttpContext httpContext,
            bool link,
            CancellationToken cancellationToken)
    {
        var state = await RequireStateAsync(httpContext);
        if (!state.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloUiSignIn>(
                state.Errors);
        }

        var result = link
            ? await oidc!.CompleteLinkAsync(
                verificationCode,
                httpContext,
                state.Value,
                CreateSessionMetadata(httpContext),
                cancellationToken)
            : await oidc!.CompleteUnlinkAsync(
                verificationCode,
                httpContext,
                state.Value,
                CreateSessionMetadata(httpContext),
                cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(MapSignIn(result.Value))
            : OperationResultFactory.Fail<HelloUiSignIn>(result.Errors);
    }

    private async Task<OperationResult<HelloOidcLocalSession>>
        RequireStateAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (oidc is null)
        {
            return Unavailable<HelloOidcLocalSession>();
        }

        return await ReadLocalSessionAsync(httpContext);
    }

    private static async Task<HelloOidcLocalSession?>
        TryReadLocalSessionAsync(HttpContext httpContext)
    {
        var result = await ReadLocalSessionAsync(httpContext);
        return result.IsSuccess ? result.Value : null;
    }

    private static async Task<OperationResult<HelloOidcLocalSession>>
        ReadLocalSessionAsync(HttpContext httpContext)
    {
        var authentication = await httpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme);
        var accessToken = authentication.Properties?.GetTokenValue(
            HelloUiDefaults.AccessTokenName);
        if (!authentication.Succeeded
            || authentication.Principal is null
            || string.IsNullOrWhiteSpace(accessToken)
            || !HelloUiPrincipalFactory.TryGetUserId(
                authentication.Principal,
                out var userId)
            || !HelloUiPrincipalFactory.TryGetSessionId(
                authentication.Principal,
                out var sessionId))
        {
            return InvalidSession<HelloOidcLocalSession>();
        }

        return OperationResultFactory.Success(
            new HelloOidcLocalSession(
                userId,
                sessionId,
                accessToken));
    }

    private HelloUiSignIn MapSignIn(HelloSignIn<TProfile> signIn)
        => new(
            HelloUiPrincipalFactory.Create(
                signIn.Account,
                signIn.Session.SessionId,
                profiles),
            signIn.Session);

    private Skopka.Identity.Sessions.IdentitySessionMetadata
        CreateSessionMetadata(HttpContext httpContext)
        => requestContext.CreateSessionMetadata(
            httpContext,
            helloOptions.ClientName);

    private static OperationResult<T> Unavailable<T>()
        => OperationResultFactory.Fail<T>(
            new Error(
                "hello.oidc.unavailable",
                "External sign-in is unavailable.",
                ErrorType.NotFound));

    private static OperationResult<T> InvalidSession<T>()
        => OperationResultFactory.Fail<T>(
            new Error(
                IdentityErrorCodes.RefreshTokenInvalid,
                "The session is invalid or expired.",
                ErrorType.Unauthorized));
}
