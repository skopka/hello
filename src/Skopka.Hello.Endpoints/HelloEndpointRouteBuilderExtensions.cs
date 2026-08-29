using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello;
using Skopka.Hello.Oidc;
using Skopka.Identity.Errors;
using Skopka.Identity.Sessions;

namespace Skopka.Hello.Endpoints;

public static class HelloEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSkopkaHello<TProfile>(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var helloOptions = endpoints.ServiceProvider
            .GetRequiredService<SkopkaHelloOptions>();
        if (helloOptions.SelfRegistrationEnabled)
        {
            endpoints.MapPost(
                    "/auth/register",
                    RegisterAsync<TProfile>)
                .WithName("SkopkaHelloRegister");
        }

        endpoints.MapPost(
                "/auth/login",
                LoginAsync<TProfile>)
            .WithName("SkopkaHelloLogin");

        var crossDeviceOptions = endpoints.ServiceProvider
            .GetRequiredService<HelloCrossDeviceSignInOptions>();
        if (crossDeviceOptions.Enabled)
        {
            endpoints.MapPost(
                    "/auth/cross-device",
                    BeginCrossDeviceSignInAsync<TProfile>)
                .AllowAnonymous()
                .WithName("SkopkaHelloBeginCrossDeviceSignIn");
            endpoints.MapGet(
                    "/auth/cross-device/{deviceCode}/status",
                    GetCrossDeviceSignInStatusAsync<TProfile>)
                .AllowAnonymous()
                .WithName("SkopkaHelloGetCrossDeviceSignInStatus");
            endpoints.MapPost(
                    "/auth/cross-device/{deviceCode}/complete",
                    CompleteCrossDeviceSignInAsync<TProfile>)
                .AllowAnonymous()
                .WithName("SkopkaHelloCompleteCrossDeviceSignIn");
            endpoints.MapGet(
                    "/account/cross-device/{deviceCode}",
                    GetCrossDeviceApprovalDetailsAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloGetCrossDeviceApprovalDetails");
            endpoints.MapPost(
                    "/account/cross-device/{deviceCode}/challenge",
                    BeginCrossDeviceApprovalAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloBeginCrossDeviceApproval");
            endpoints.MapPost(
                    "/account/cross-device/{deviceCode}/approve",
                    ApproveCrossDeviceSignInAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloApproveCrossDeviceSignIn");
            endpoints.MapPost(
                    "/account/cross-device/{deviceCode}/deny",
                    DenyCrossDeviceSignInAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloDenyCrossDeviceSignIn");
        }

        endpoints.MapGet(
                "/auth/antiforgery",
                IssueAuthenticatedAntiforgery)
            .RequireAuthorization()
            .WithName("SkopkaHelloIssueAuthenticatedAntiforgery");

        var oidcEnabled = endpoints.ServiceProvider.GetService<
            IHelloOidcProviderCatalog>() is not null;
        if (oidcEnabled)
        {
            endpoints.MapGet(
                    "/auth/external/providers",
                    GetExternalProviders)
                .AllowAnonymous()
                .WithName("SkopkaHelloGetExternalProviders");

            endpoints.MapGet(
                    "/auth/external/{providerId}/challenge",
                    BeginExternalSignIn)
                .AllowAnonymous()
                .WithName("SkopkaHelloBeginExternalSignIn");

            endpoints.MapGet(
                    HelloOidcDefaults.ApiLinkChallengePath,
                    BeginExternalLinkAsync<TProfile>)
                .AllowAnonymous()
                .WithName("SkopkaHelloBeginExternalLink");

            endpoints.MapPost(
                    HelloOidcDefaults.ApiCompletionPath,
                    CompleteExternalSignInAsync<TProfile>)
                .AllowAnonymous()
                .WithName("SkopkaHelloCompleteExternalSignIn");

            if (helloOptions.SelfRegistrationEnabled)
            {
                endpoints.MapGet(
                        HelloOidcDefaults.ApiRegistrationPath,
                        GetExternalRegistrationAsync<TProfile>)
                    .AllowAnonymous()
                    .WithName("SkopkaHelloGetExternalRegistration");

                endpoints.MapPost(
                        HelloOidcDefaults.ApiRegistrationPath,
                        RegisterExternalAsync<TProfile>)
                    .AllowAnonymous()
                    .WithName("SkopkaHelloRegisterExternal");
            }

            endpoints.MapDelete(
                    "/auth/external/flow",
                    CancelExternalFlowAsync<TProfile>)
                .AllowAnonymous()
                .WithName("SkopkaHelloCancelExternalFlow");
        }

        endpoints.MapPost(
                "/auth/refresh",
                RefreshAsync<TProfile>)
            .WithName("SkopkaHelloRefresh");

        endpoints.MapPost(
                "/auth/logout",
                LogoutAsync<TProfile>)
            .WithName("SkopkaHelloLogout");

        endpoints.MapPost(
                "/auth/logout-all",
                LogoutAllAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloLogoutAll");

        endpoints.MapPost(
                "/auth/password-reset/request",
                RequestPasswordResetAsync<TProfile>)
            .WithName("SkopkaHelloRequestPasswordReset");

        endpoints.MapPost(
                "/auth/password-reset/confirm",
                ResetPasswordAsync<TProfile>)
            .WithName("SkopkaHelloResetPassword");

        endpoints.MapPost(
                "/auth/email-confirmation/request",
                RequestEmailConfirmationAsync<TProfile>)
            .WithName("SkopkaHelloRequestEmailConfirmation");

        endpoints.MapPost(
                "/auth/email-confirmation/confirm",
                ConfirmEmailAsync<TProfile>)
            .WithName("SkopkaHelloConfirmEmail");

        endpoints.MapPost(
                "/auth/phone-confirmation/request",
                RequestPhoneConfirmationAsync<TProfile>)
            .WithName("SkopkaHelloRequestPhoneConfirmation");

        endpoints.MapPost(
                "/auth/phone-confirmation/confirm",
                ConfirmPhoneAsync<TProfile>)
            .WithName("SkopkaHelloConfirmPhone");

        endpoints.MapGet(
                "/account/me",
                GetMeAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloGetMe");

        endpoints.MapPut(
                "/account/user-name",
                ChangeUserNameAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloChangeUserName");

        endpoints.MapPut(
                "/account/email",
                ChangeEmailAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloChangeEmail");

        endpoints.MapPut(
                "/account/phone",
                ChangePhoneAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloChangePhone");

        endpoints.MapPut(
                "/account/profile",
                ReplaceProfileAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloReplaceProfile");

        endpoints.MapGet(
                "/account/sessions",
                GetSessionsAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloGetSessions");

        if (oidcEnabled)
        {
            endpoints.MapGet(
                    "/account/external-logins",
                    GetExternalLoginsAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloGetExternalLogins");

            endpoints.MapPost(
                    "/account/external-logins/{providerId}/link",
                    PrepareExternalLinkAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloPrepareExternalLink");

            endpoints.MapPost(
                    "/account/external-logins/link/challenge",
                    BeginExternalLinkVerificationAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloBeginExternalLinkVerification");

            endpoints.MapPut(
                    "/account/external-logins/link",
                    CompleteExternalLinkAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloCompleteExternalLink");

            endpoints.MapPost(
                    "/account/external-logins/{providerId}/unlink/challenge",
                    BeginExternalUnlinkAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloBeginExternalUnlink");

            endpoints.MapDelete(
                    "/account/external-logins/unlink",
                    CompleteExternalUnlinkAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloCompleteExternalUnlink");
        }

        endpoints.MapDelete(
                "/account/sessions/{sessionId:guid}",
                DeleteSessionAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloDeleteSession");

        if (helloOptions.Totp.Enabled)
        {
            endpoints.MapGet(
                    "/account/authenticator",
                    GetTotpStateAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloGetAuthenticator");

            endpoints.MapPost(
                    "/account/authenticator/enrollment",
                    BeginTotpEnrollmentAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloBeginAuthenticatorEnrollment");

            endpoints.MapPost(
                    "/account/authenticator/enrollment/confirm",
                    ConfirmTotpEnrollmentAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloConfirmAuthenticatorEnrollment");

            endpoints.MapPost(
                    "/account/authenticator/remove/challenge",
                    BeginTotpDisableAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloBeginAuthenticatorRemoval");

            endpoints.MapDelete(
                    "/account/authenticator",
                    CompleteTotpDisableAsync<TProfile>)
                .RequireAuthorization()
                .WithName("SkopkaHelloCompleteAuthenticatorRemoval");
        }

        endpoints.MapPost(
                "/account/password/change/challenge",
                BeginPasswordChangeAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloBeginPasswordChange");

        endpoints.MapPost(
                "/account/password/change",
                CompletePasswordChangeAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloCompletePasswordChange");

        endpoints.MapPost(
                "/account/password/set/challenge",
                BeginPasswordSetAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloBeginPasswordSet");

        endpoints.MapPut(
                "/account/password",
                CompletePasswordSetAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloCompletePasswordSet");

        endpoints.MapPost(
                "/account/password/remove/challenge",
                BeginPasswordRemovalAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloBeginPasswordRemoval");

        endpoints.MapDelete(
                "/account/password",
                CompletePasswordRemovalAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloCompletePasswordRemoval");

        endpoints.MapPost(
                "/account/delete/challenge",
                BeginAccountDeletionAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloBeginAccountDeletion");

        endpoints.MapDelete(
                "/account",
                CompleteAccountDeletionAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloCompleteAccountDeletion");

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync<TProfile>(
        RegisterRequest<TProfile> request,
        IHelloIdentityApplication<TProfile> application,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await application.RegisterAsync(
            new HelloRegisterCommand<TProfile>(
                request.UserName,
                request.Email,
                request.Phone,
                request.Profile,
                request.Password)
            {
                RegistrationConsent = CreateRegistrationConsent(
                    request.AcceptTermsOfService,
                    request.AcceptPrivacyPolicy,
                    timeProvider),
            },
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created(
                "/account/me",
                ToAccountResponse(result.Value))
            : OperationResultProblemMapper.ToResult(result, httpContext);
    }

    private static async Task<IResult> LoginAsync<TProfile>(
        LoginRequest request,
        IHelloIdentityApplication<TProfile> application,
        IHelloRequestContext requestContext,
        SkopkaHelloOptions options,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var transport = cookies.ValidateTransport(httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        var authenticated = await application.LoginAsync(
            new HelloLoginCommand(
                request.Login,
                request.Password,
                requestContext.CreateClientKey(httpContext),
                requestContext.CreateSessionMetadata(
                    httpContext,
                    options.ClientName)),
            cancellationToken);
        if (!authenticated.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                authenticated,
                httpContext);
        }

        cookies.WriteSessionCookies(
            httpContext,
            authenticated.Value.Session);
        return TypedResults.Ok(
            ToSessionResponse(authenticated.Value.Session));
    }

    private static async Task<IResult> BeginCrossDeviceSignInAsync<TProfile>(
        BeginCrossDeviceSignInRequest request,
        IHelloCrossDeviceSignInApplication<TProfile> application,
        IHelloCrossDeviceCookieManager verifierCookies,
        IHelloSessionCookieManager sessionCookies,
        IHelloRequestContext requestContext,
        SkopkaHelloOptions helloOptions,
        HelloCrossDeviceSignInOptions crossDeviceOptions,
        HelloUiRoutePaths routes,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var transport = sessionCookies.ValidateTransport(httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        var clientKey = requestContext.CreateClientKey(httpContext);
        var metadata = requestContext.CreateSessionMetadata(
            httpContext,
            crossDeviceOptions.SessionClientName
                ?? helloOptions.ClientName);
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        var result = await application.BeginAsync(
            new HelloBeginCrossDeviceSignInCommand(
                request.ReturnUrl,
                request.ClientId,
                clientKey,
                string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
                metadata.DeviceName,
                metadata,
                clientKey),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                result,
                httpContext);
        }

        verifierCookies.Write(
            httpContext,
            result.Value.DeviceCode,
            result.Value.BrowserVerifier,
            result.Value.ExpiresAt);
        return TypedResults.Ok(
            new BeginCrossDeviceSignInResponse(
                result.Value.RequestId,
                result.Value.DeviceCode,
                result.Value.UserCode,
                CreateCrossDeviceApprovalUrl(
                    httpContext,
                    helloOptions,
                    routes,
                    result.Value.DeviceCode),
                result.Value.CreatedAt,
                result.Value.ExpiresAt));
    }

    private static async Task<IResult>
        GetCrossDeviceSignInStatusAsync<TProfile>(
            string deviceCode,
            IHelloCrossDeviceSignInApplication<TProfile> application,
            IHelloCrossDeviceCookieManager verifierCookies,
            IHelloRequestContext requestContext,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        if (!verifierCookies.TryRead(
                httpContext,
                deviceCode,
                out var browserVerifier))
        {
            return InvalidCrossDeviceRequest(httpContext);
        }

        var result = await application.GetStatusAsync(
            deviceCode,
            browserVerifier,
            requestContext.CreateClientKey(httpContext),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(
                new CrossDeviceSignInStatusResponse(
                    result.Value.State.ToString().ToLowerInvariant(),
                    result.Value.UserCode,
                    result.Value.CreatedAt,
                    result.Value.ExpiresAt,
                    result.Value.ResolvedAt))
            : OperationResultProblemMapper.ToResult(result, httpContext);
    }

    private static async Task<IResult>
        GetCrossDeviceApprovalDetailsAsync<TProfile>(
            string deviceCode,
            IHelloCrossDeviceSignInApplication<TProfile> application,
            IHelloRequestContext requestContext,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.GetApprovalDetailsAsync(
            accessToken,
            deviceCode,
            requestContext.CreateClientKey(httpContext),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(
                new CrossDeviceApprovalDetailsResponse(
                    result.Value.DeviceCode,
                    result.Value.UserCode,
                    result.Value.CreatedAt,
                    result.Value.ExpiresAt,
                    result.Value.IpAddress,
                    result.Value.UserAgent,
                    result.Value.DeviceDisplayName))
            : OperationResultProblemMapper.ToResult(result, httpContext);
    }

    private static async Task<IResult>
        BeginCrossDeviceApprovalAsync<TProfile>(
            string deviceCode,
            IHelloCrossDeviceSignInApplication<TProfile> application,
            IHelloRequestContext requestContext,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.BeginApprovalAsync(
            new HelloBeginCrossDeviceApprovalCommand(
                accessToken,
                deviceCode,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return ToStepUpResult(result, httpContext);
    }

    private static async Task<IResult> ApproveCrossDeviceSignInAsync<TProfile>(
        string deviceCode,
        ApproveCrossDeviceSignInRequest request,
        IHelloCrossDeviceSignInApplication<TProfile> application,
        IHelloRequestContext requestContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.ApproveAsync(
            new HelloApproveCrossDeviceSignInCommand(
                accessToken,
                deviceCode,
                request.ChallengeId,
                request.TotpCode,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.NoContent()
            : OperationResultProblemMapper.ToResult(result, httpContext);
    }

    private static async Task<IResult> DenyCrossDeviceSignInAsync<TProfile>(
        string deviceCode,
        IHelloCrossDeviceSignInApplication<TProfile> application,
        IHelloRequestContext requestContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.DenyAsync(
            new HelloDenyCrossDeviceSignInCommand(
                accessToken,
                deviceCode,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.NoContent()
            : OperationResultProblemMapper.ToResult(result, httpContext);
    }

    private static async Task<IResult>
        CompleteCrossDeviceSignInAsync<TProfile>(
            string deviceCode,
            IHelloCrossDeviceSignInApplication<TProfile> application,
            IHelloCrossDeviceCookieManager verifierCookies,
            IHelloSessionCookieManager sessionCookies,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var transport = sessionCookies.ValidateTransport(httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        if (!verifierCookies.TryRead(
                httpContext,
                deviceCode,
                out var browserVerifier))
        {
            return InvalidCrossDeviceRequest(httpContext);
        }

        var result = await application.CompleteAsync(
            deviceCode,
            browserVerifier,
            cancellationToken);
        if (!result.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                result,
                httpContext);
        }

        verifierCookies.Delete(httpContext);
        sessionCookies.WriteSessionCookies(
            httpContext,
            result.Value.SignIn.Session);
        return TypedResults.Ok(
            new CompleteCrossDeviceSignInResponse(
                ToSessionResponse(result.Value.SignIn.Session),
                result.Value.ClientId,
                result.Value.ReturnUrl));
    }

    private static string CreateCrossDeviceApprovalUrl(
        HttpContext httpContext,
        SkopkaHelloOptions helloOptions,
        HelloUiRoutePaths routes,
        string deviceCode)
    {
        var path = QueryHelpers.AddQueryString(
            routes.CrossDeviceApprovalPath,
            "deviceCode",
            deviceCode);
        return helloOptions.PublicOrigin is not null
            ? new Uri(helloOptions.PublicOrigin, path).AbsoluteUri
            : $"{httpContext.Request.Scheme}://"
                + $"{httpContext.Request.Host}"
                + $"{httpContext.Request.PathBase}{path}";
    }

    private static IResult IssueAuthenticatedAntiforgery(
        IHelloAntiforgeryTokenIssuer antiforgeryTokens,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var transport = cookies.ValidateTransport(httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        antiforgeryTokens.Issue(httpContext);
        return TypedResults.NoContent();
    }

    private static Microsoft.AspNetCore.Http.HttpResults.Ok<
        ExternalProviderResponse[]> GetExternalProviders(
        IHelloOidcProviderCatalog providers)
        => TypedResults.Ok(
            providers.Providers
                .Select(provider => new ExternalProviderResponse(
                    provider.Id,
                    provider.DisplayName))
                .ToArray());

    private static IResult BeginExternalSignIn(
        string providerId,
        string returnUrl,
        IHelloOidcChallengeService challenges,
        IHelloAntiforgeryTokenIssuer antiforgeryTokens,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var transport = cookies.ValidateTransport(httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        var challenge = challenges.CreateHeadlessSignIn(
            providerId,
            returnUrl);
        if (!challenge.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                challenge,
                httpContext);
        }

        antiforgeryTokens.Issue(httpContext);
        return Results.Challenge(
            challenge.Value.Properties,
            [challenge.Value.AuthenticationScheme]);
    }

    private static async Task<IResult> BeginExternalLinkAsync<TProfile>(
        string providerId,
        IHelloOidcApplication<TProfile> application,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var transport = cookies.ValidateTransport(httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        var challenge = await application.BeginHeadlessLinkAsync(
            providerId,
            httpContext,
            cancellationToken);
        return challenge.IsSuccess
            ? Results.Challenge(
                challenge.Value.Properties,
                [challenge.Value.AuthenticationScheme])
            : OperationResultProblemMapper.ToResult(
                challenge,
                httpContext);
    }

    private static async Task<IResult>
        CompleteExternalSignInAsync<TProfile>(
            IHelloOidcApplication<TProfile> application,
            IHelloRequestContext requestContext,
            SkopkaHelloOptions options,
            IHelloSessionCookieManager cookies,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var transport = cookies.ValidateTransport(httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        var csrf = await cookies.ValidateAntiforgeryAsync(httpContext);
        if (!csrf.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                csrf,
                httpContext);
        }

        var completed = await application.CompleteChallengeAsync(
            httpContext,
            TryReadBearerSession(httpContext),
            requestContext.CreateSessionMetadata(
                httpContext,
                options.ClientName),
            cancellationToken);
        if (!completed.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                completed,
                httpContext);
        }

        return completed.Value.Kind switch
        {
            HelloOidcCompletionKind.SignedIn
                when completed.Value.SignIn is not null =>
                    FinishExternalSignIn(
                        completed.Value.SignIn,
                        completed.Value.ReturnUrl,
                        cookies,
                        httpContext),
            HelloOidcCompletionKind.RegistrationRequired
                when completed.Value.Registration is not null =>
                    TypedResults.Ok(
                        ToExternalRegistrationResponse(
                            completed.Value.Registration)),
            HelloOidcCompletionKind.LinkPending
                when completed.Value.Provider is not null =>
                    TypedResults.Ok(
                        ToExternalLinkResponse(
                            completed.Value.Provider,
                            completed.Value.ReturnUrl)),
            _ => InvalidExternalFlow(httpContext),
        };
    }

    private static async Task<IResult>
        GetExternalRegistrationAsync<TProfile>(
            IHelloOidcApplication<TProfile> application,
            IHelloSessionCookieManager cookies,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var transport = cookies.ValidateTransport(httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        var hints = await application.GetRegistrationHintsAsync(
            httpContext,
            cancellationToken);
        return hints.IsSuccess
            ? TypedResults.Ok(
                ToExternalRegistrationResponse(hints.Value))
            : OperationResultProblemMapper.ToResult(
                hints,
                httpContext);
    }

    private static async Task<IResult> RegisterExternalAsync<TProfile>(
        ExternalRegisterRequest<TProfile> request,
        IHelloOidcApplication<TProfile> application,
        IHelloRequestContext requestContext,
        SkopkaHelloOptions options,
        IHelloSessionCookieManager cookies,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var transport = cookies.ValidateTransport(httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        var csrf = await cookies.ValidateAntiforgeryAsync(httpContext);
        if (!csrf.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                csrf,
                httpContext);
        }

        var hints = await application.GetRegistrationHintsAsync(
            httpContext,
            cancellationToken);
        if (!hints.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                hints,
                httpContext);
        }

        var registered = await application.RegisterAsync(
            new HelloOidcRegisterCommand<TProfile>(
                request.UserName,
                request.Email,
                request.Phone,
                request.Profile,
                requestContext.CreateSessionMetadata(
                    httpContext,
                    options.ClientName))
            {
                RegistrationConsent = CreateRegistrationConsent(
                    request.AcceptTermsOfService,
                    request.AcceptPrivacyPolicy,
                    timeProvider),
            },
            httpContext,
            cancellationToken);
        return registered.IsSuccess
            ? FinishExternalSignIn(
                registered.Value,
                hints.Value.ReturnUrl,
                cookies,
                httpContext)
            : OperationResultProblemMapper.ToResult(
                registered,
                httpContext);
    }

    private static async Task<IResult> CancelExternalFlowAsync<TProfile>(
        IHelloOidcApplication<TProfile> application,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var transport = cookies.ValidateTransport(httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        var csrf = await cookies.ValidateAntiforgeryAsync(httpContext);
        if (!csrf.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                csrf,
                httpContext);
        }

        await application.ClearBrowserFlowAsync(
            httpContext,
            cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RefreshAsync<TProfile>(
        IHelloIdentityApplication<TProfile> application,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var transport = cookies.ValidateTransport(httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        var csrf = await cookies.ValidateAntiforgeryAsync(httpContext);
        if (!csrf.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                csrf,
                httpContext);
        }

        var refreshToken = cookies.ReadRefreshToken(httpContext);
        if (refreshToken is null)
        {
            return InvalidSession(httpContext);
        }

        var refreshed = await application.RefreshAsync(
            refreshToken,
            cancellationToken);
        if (!refreshed.IsSuccess)
        {
            cookies.DeleteSessionCookies(httpContext);
            return OperationResultProblemMapper.ToResult(
                refreshed,
                httpContext);
        }

        cookies.WriteSessionCookies(httpContext, refreshed.Value);
        return TypedResults.Ok(ToSessionResponse(refreshed.Value));
    }

    private static async Task<IResult> LogoutAsync<TProfile>(
        IHelloIdentityApplication<TProfile> application,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var transport = cookies.ValidateTransport(httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        var csrf = await cookies.ValidateAntiforgeryAsync(httpContext);
        if (!csrf.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                csrf,
                httpContext);
        }

        var refreshToken = cookies.ReadRefreshToken(httpContext);
        if (refreshToken is null)
        {
            cookies.DeleteSessionCookies(httpContext);
            return TypedResults.NoContent();
        }

        var revoked = await application.LogoutAsync(
            refreshToken,
            cancellationToken);
        cookies.DeleteSessionCookies(httpContext);

        return revoked.IsSuccess
            ? TypedResults.NoContent()
            : OperationResultProblemMapper.ToResult(
                revoked,
                httpContext);
    }

    private static async Task<IResult> LogoutAllAsync<TProfile>(
        IHelloIdentityApplication<TProfile> application,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var validated = await application.ValidateAccessTokenAsync(
            accessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                validated,
                httpContext);
        }

        var revoked = await application.LogoutAllAsync(
            validated.Value.Id,
            cancellationToken);
        if (!revoked.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                revoked,
                httpContext);
        }

        cookies.DeleteSessionCookies(httpContext);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RequestPasswordResetAsync<TProfile>(
        RequestAccountMessageRequest request,
        IHelloIdentityApplication<TProfile> application,
        IHelloRequestContext requestContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await application.RequestPasswordResetAsync(
            request.Email,
            requestContext.CreateClientKey(httpContext),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Accepted((string?)null)
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);
    }

    private static async Task<IResult> ResetPasswordAsync<TProfile>(
        ResetPasswordRequest request,
        IHelloIdentityApplication<TProfile> application,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await application.ResetPasswordAsync(
            new HelloResetPasswordCommand(
                request.UserId,
                request.Token,
                request.NewPassword),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                result,
                httpContext);
        }

        cookies.DeleteSessionCookies(httpContext);
        return TypedResults.NoContent();
    }

    private static async Task<IResult>
        RequestEmailConfirmationAsync<TProfile>(
            RequestAccountMessageRequest request,
            IHelloIdentityApplication<TProfile> application,
            IHelloRequestContext requestContext,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var result = await application.RequestEmailConfirmationAsync(
            request.Email,
            requestContext.CreateClientKey(httpContext),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Accepted((string?)null)
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);
    }

    private static async Task<IResult> ConfirmEmailAsync<TProfile>(
        ConfirmEmailRequest request,
        IHelloIdentityApplication<TProfile> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await application.ConfirmEmailAsync(
            new HelloConfirmEmailCommand(
                request.UserId,
                request.Email,
                request.Token),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.NoContent()
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);
    }

    private static async Task<IResult>
        RequestPhoneConfirmationAsync<TProfile>(
            RequestPhoneConfirmationRequest request,
            IHelloIdentityApplication<TProfile> application,
            IHelloRequestContext requestContext,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var result = await application.RequestPhoneConfirmationAsync(
            request.Phone,
            requestContext.CreateClientKey(httpContext),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Accepted((string?)null)
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);
    }

    private static async Task<IResult> ConfirmPhoneAsync<TProfile>(
        ConfirmPhoneRequest request,
        IHelloIdentityApplication<TProfile> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await application.ConfirmPhoneAsync(
            new HelloConfirmPhoneCommand(
                request.UserId,
                request.Phone,
                request.Token),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.NoContent()
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);
    }

    private static async Task<IResult> GetMeAsync<TProfile>(
        IHelloIdentityApplication<TProfile> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var validated = await application.ValidateAccessTokenAsync(
            accessToken,
            cancellationToken);
        return validated.IsSuccess
            ? TypedResults.Ok(ToAccountResponse(validated.Value))
            : OperationResultProblemMapper.ToResult(
                validated,
                httpContext);
    }

    private static async Task<IResult> GetSessionsAsync<TProfile>(
        IHelloIdentityApplication<TProfile> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var validated = await application.ValidateAccessTokenAsync(
            accessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                validated,
                httpContext);
        }

        var listed = await application.ListSessionsAsync(
            validated.Value.Id,
            cancellationToken);
        return listed.IsSuccess
            ? TypedResults.Ok(
                listed.Value
                    .Select(ToSessionInfoResponse)
                    .ToArray())
            : OperationResultProblemMapper.ToResult(
                listed,
                httpContext);
    }

    private static async Task<IResult> GetExternalLoginsAsync<TProfile>(
        IHelloOidcApplication<TProfile> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var listed = await application.ListLinkedProvidersAsync(
            accessToken,
            cancellationToken);
        if (!listed.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                listed,
                httpContext);
        }

        return TypedResults.Ok(
            listed.Value
                .Select(provider =>
                    new LinkedExternalProviderResponse(
                        provider.ProviderId,
                        provider.DisplayName,
                        provider.Enabled,
                        provider.CanUnlink,
                        provider.LinkedAt))
                .ToArray());
    }

    private static async Task<IResult> PrepareExternalLinkAsync<TProfile>(
        string providerId,
        ExternalLinkRequest request,
        IHelloOidcApplication<TProfile> application,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var transport = cookies.ValidateTransport(httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        var csrf = await cookies.ValidateAntiforgeryAsync(httpContext);
        if (!csrf.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                csrf,
                httpContext);
        }

        var localSession = TryReadBearerSession(httpContext);
        if (localSession is null)
        {
            return InvalidSession(httpContext);
        }

        var prepared = await application.PrepareHeadlessLinkAsync(
            providerId,
            request.ReturnUrl,
            localSession,
            httpContext,
            cancellationToken);
        return prepared.IsSuccess
            ? TypedResults.Ok(
                new ExternalLinkStartResponse(
                    prepared.Value.ChallengeUrl))
            : OperationResultProblemMapper.ToResult(
                prepared,
                httpContext);
    }

    private static async Task<IResult>
        BeginExternalLinkVerificationAsync<TProfile>(
            IHelloOidcApplication<TProfile> application,
            IHelloRequestContext requestContext,
            IHelloSessionCookieManager cookies,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var validation = await ValidateExternalCookieMutationAsync(
            cookies,
            httpContext);
        if (!validation.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                validation,
                httpContext);
        }

        var localSession = TryReadBearerSession(httpContext);
        if (localSession is null)
        {
            return InvalidSession(httpContext);
        }

        var begun = await application.BeginLinkAsync(
            httpContext,
            localSession,
            requestContext.CreateClientKey(httpContext),
            cancellationToken);
        return ToStepUpResult(begun, httpContext);
    }

    private static async Task<IResult> CompleteExternalLinkAsync<TProfile>(
        ExternalLoginMutationRequest request,
        IHelloOidcApplication<TProfile> application,
        IHelloRequestContext requestContext,
        SkopkaHelloOptions options,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var validation = await ValidateExternalCookieMutationAsync(
            cookies,
            httpContext);
        if (!validation.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                validation,
                httpContext);
        }

        var localSession = TryReadBearerSession(httpContext);
        if (localSession is null)
        {
            return InvalidSession(httpContext);
        }

        var completed = await application.CompleteLinkAsync(
            request.VerificationCode,
            httpContext,
            localSession,
            requestContext.CreateSessionMetadata(
                httpContext,
                options.ClientName),
            cancellationToken);
        return await FinishExternalLoginMutationAsync(
            completed,
            application,
            cookies,
            httpContext,
            cancellationToken);
    }

    private static async Task<IResult> BeginExternalUnlinkAsync<TProfile>(
        string providerId,
        IHelloOidcApplication<TProfile> application,
        IHelloRequestContext requestContext,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var validation = await ValidateExternalCookieMutationAsync(
            cookies,
            httpContext);
        if (!validation.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                validation,
                httpContext);
        }

        var localSession = TryReadBearerSession(httpContext);
        if (localSession is null)
        {
            return InvalidSession(httpContext);
        }

        var begun = await application.BeginUnlinkAsync(
            providerId,
            httpContext,
            localSession,
            requestContext.CreateClientKey(httpContext),
            cancellationToken);
        return ToStepUpResult(begun, httpContext);
    }

    private static async Task<IResult>
        CompleteExternalUnlinkAsync<TProfile>(
            [FromBody] ExternalLoginMutationRequest request,
            IHelloOidcApplication<TProfile> application,
            IHelloRequestContext requestContext,
            SkopkaHelloOptions options,
            IHelloSessionCookieManager cookies,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var validation = await ValidateExternalCookieMutationAsync(
            cookies,
            httpContext);
        if (!validation.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                validation,
                httpContext);
        }

        var localSession = TryReadBearerSession(httpContext);
        if (localSession is null)
        {
            return InvalidSession(httpContext);
        }

        var completed = await application.CompleteUnlinkAsync(
            request.VerificationCode,
            httpContext,
            localSession,
            requestContext.CreateSessionMetadata(
                httpContext,
                options.ClientName),
            cancellationToken);
        return await FinishExternalLoginMutationAsync(
            completed,
            application,
            cookies,
            httpContext,
            cancellationToken);
    }

    private static async Task<IResult> DeleteSessionAsync<TProfile>(
        Guid sessionId,
        IHelloIdentityApplication<TProfile> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var validated = await application.ValidateAccessTokenAsync(
            accessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                validated,
                httpContext);
        }

        var revoked = await application.RevokeSessionAsync(
            validated.Value.Id,
            sessionId,
            cancellationToken);
        return revoked.IsSuccess
            ? TypedResults.NoContent()
            : OperationResultProblemMapper.ToResult(
                revoked,
                httpContext);
    }

    private static async Task<IResult>
        BeginPasswordChangeAsync<TProfile>(
            IHelloIdentityApplication<TProfile> application,
            IHelloRequestContext requestContext,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.BeginPasswordChangeAsync(
            new HelloBeginPasswordChangeCommand(
                accessToken,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(
                new StepUpChallengeResponse(
                    result.Value.ChallengeId,
                    result.Value.ExpiresAt,
                    result.Value.DeliveryChannel.ToString()
                        .ToLowerInvariant()))
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);
    }

    private static async Task<IResult> GetTotpStateAsync<TProfile>(
        IHelloIdentityApplication<TProfile> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.GetTotpStateAsync(
            accessToken,
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(ToTotpStateResponse(result.Value))
            : OperationResultProblemMapper.ToResult(result, httpContext);
    }

    private static async Task<IResult> BeginTotpEnrollmentAsync<TProfile>(
        IHelloIdentityApplication<TProfile> application,
        IHelloRequestContext requestContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.BeginTotpEnrollmentAsync(
            new HelloBeginTotpEnrollmentCommand(
                accessToken,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(
                new TotpEnrollmentResponse(
                    result.Value.EnrollmentId,
                    result.Value.Secret,
                    result.Value.ProvisioningUri,
                    result.Value.QrCodeSvg,
                    result.Value.ExpiresAt))
            : OperationResultProblemMapper.ToResult(result, httpContext);
    }

    private static async Task<IResult> ConfirmTotpEnrollmentAsync<TProfile>(
        ConfirmTotpEnrollmentRequest request,
        IHelloIdentityApplication<TProfile> application,
        IHelloRequestContext requestContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.ConfirmTotpEnrollmentAsync(
            new HelloConfirmTotpEnrollmentCommand(
                accessToken,
                request.EnrollmentId,
                request.Code,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(
                new ConfirmedTotpEnrollmentResponse(
                    ToTotpStateResponse(result.Value.State),
                    result.Value.RecoveryCodes))
            : OperationResultProblemMapper.ToResult(result, httpContext);
    }

    private static async Task<IResult> BeginTotpDisableAsync<TProfile>(
        IHelloIdentityApplication<TProfile> application,
        IHelloRequestContext requestContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.BeginTotpDisableAsync(
            new HelloBeginTotpDisableCommand(
                accessToken,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return ToStepUpResult(result, httpContext);
    }

    private static async Task<IResult> CompleteTotpDisableAsync<TProfile>(
        [FromBody] CompleteAccountSecurityActionRequest request,
        IHelloIdentityApplication<TProfile> application,
        IHelloRequestContext requestContext,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.CompleteTotpDisableAsync(
            new HelloCompleteTotpDisableCommand(
                accessToken,
                request.ChallengeId,
                request.VerificationCode,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return FinishSessionEndingMutation(result, cookies, httpContext);
    }

    private static async Task<IResult>
        CompletePasswordChangeAsync<TProfile>(
            ChangePasswordRequest request,
            IHelloIdentityApplication<TProfile> application,
            IHelloRequestContext requestContext,
            IHelloSessionCookieManager cookies,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.CompletePasswordChangeAsync(
            new HelloCompletePasswordChangeCommand(
                accessToken,
                request.ChallengeId,
                request.VerificationCode,
                request.CurrentPassword,
                request.NewPassword,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                result,
                httpContext);
        }

        cookies.DeleteSessionCookies(httpContext);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> BeginPasswordSetAsync<TProfile>(
        IHelloIdentityApplication<TProfile> application,
        IHelloRequestContext requestContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.BeginPasswordSetAsync(
            new HelloBeginPasswordSetCommand(
                accessToken,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return ToStepUpResult(result, httpContext);
    }

    private static async Task<IResult> CompletePasswordSetAsync<TProfile>(
        SetPasswordRequest request,
        IHelloIdentityApplication<TProfile> application,
        IHelloRequestContext requestContext,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.CompletePasswordSetAsync(
            new HelloCompletePasswordSetCommand(
                accessToken,
                request.ChallengeId,
                request.VerificationCode,
                request.NewPassword,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return FinishSessionEndingMutation(
            result,
            cookies,
            httpContext);
    }

    private static async Task<IResult>
        BeginPasswordRemovalAsync<TProfile>(
            IHelloIdentityApplication<TProfile> application,
            IHelloRequestContext requestContext,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.BeginPasswordRemovalAsync(
            new HelloBeginPasswordRemovalCommand(
                accessToken,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return ToStepUpResult(result, httpContext);
    }

    private static async Task<IResult>
        CompletePasswordRemovalAsync<TProfile>(
            [FromBody] CompleteAccountSecurityActionRequest request,
            IHelloIdentityApplication<TProfile> application,
            IHelloRequestContext requestContext,
            IHelloSessionCookieManager cookies,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.CompletePasswordRemovalAsync(
            new HelloCompletePasswordRemovalCommand(
                accessToken,
                request.ChallengeId,
                request.VerificationCode,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return FinishSessionEndingMutation(
            result,
            cookies,
            httpContext);
    }

    private static async Task<IResult> BeginAccountDeletionAsync<TProfile>(
        IHelloIdentityApplication<TProfile> application,
        IHelloRequestContext requestContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.BeginAccountDeletionAsync(
            new HelloBeginAccountDeletionCommand(
                accessToken,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return ToStepUpResult(result, httpContext);
    }

    private static async Task<IResult>
        CompleteAccountDeletionAsync<TProfile>(
            [FromBody] CompleteAccountSecurityActionRequest request,
            IHelloIdentityApplication<TProfile> application,
            IHelloRequestContext requestContext,
            IHelloSessionCookieManager cookies,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.CompleteAccountDeletionAsync(
            new HelloCompleteAccountDeletionCommand(
                accessToken,
                request.ChallengeId,
                request.VerificationCode,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return FinishSessionEndingMutation(
            result,
            cookies,
            httpContext);
    }

    private static Microsoft.AspNetCore.Http.HttpResults.Ok<
        ExternalAuthenticationResponse> FinishExternalSignIn<TProfile>(
        HelloSignIn<TProfile> signIn,
        string returnUrl,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext)
    {
        cookies.WriteSessionCookies(httpContext, signIn.Session);
        return TypedResults.Ok(
            new ExternalAuthenticationResponse(
                ExternalAuthenticationOutcome.SignedIn,
                ToSessionResponse(signIn.Session),
                null,
                null,
                returnUrl));
    }

    private static ExternalAuthenticationResponse
        ToExternalRegistrationResponse(
            HelloOidcRegistrationHints hints)
        => new(
            ExternalAuthenticationOutcome.RegistrationRequired,
            null,
            new ExternalRegistrationHintsResponse(
                new ExternalProviderResponse(
                    hints.Provider.Id,
                    hints.Provider.DisplayName),
                hints.DisplayName,
                hints.VerifiedEmail,
                hints.Locale),
            null,
            hints.ReturnUrl);

    private static ExternalAuthenticationResponse ToExternalLinkResponse(
        HelloOidcProvider provider,
        string returnUrl)
        => new(
            ExternalAuthenticationOutcome.LinkVerificationRequired,
            null,
            null,
            new ExternalProviderResponse(
                provider.Id,
                provider.DisplayName),
            returnUrl);

    private static async Task<OperationResult>
        ValidateExternalCookieMutationAsync(
            IHelloSessionCookieManager cookies,
            HttpContext httpContext)
    {
        var transport = cookies.ValidateTransport(httpContext);
        return transport.IsSuccess
            ? await cookies.ValidateAntiforgeryAsync(httpContext)
            : transport;
    }

    private static async Task<IResult>
        FinishExternalLoginMutationAsync<TProfile>(
            OperationResult<HelloSignIn<TProfile>> result,
            IHelloOidcApplication<TProfile> application,
            IHelloSessionCookieManager cookies,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            cookies.WriteSessionCookies(
                httpContext,
                result.Value.Session);
            return TypedResults.Ok(
                ToSessionResponse(result.Value.Session));
        }

        var challengeRestartRequired = result.Errors.Any(error =>
            string.Equals(
                error.Code,
                HelloExternalIdentityErrorCodes
                    .ChallengeRestartRequired,
                StringComparison.Ordinal));
        var sessionRestartRequired = result.Errors.Any(error =>
            string.Equals(
                error.Code,
                HelloExternalIdentityErrorCodes.RestartRequired,
                StringComparison.Ordinal));
        if (challengeRestartRequired || sessionRestartRequired)
        {
            await application.ClearBrowserFlowAsync(
                httpContext,
                cancellationToken);
        }

        if (sessionRestartRequired)
        {
            cookies.DeleteSessionCookies(httpContext);
        }

        return OperationResultProblemMapper.ToResult(
            result,
            httpContext);
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult
        InvalidExternalFlow(HttpContext httpContext)
        => OperationResultProblemMapper.ToResult(
            OperationResultFactory.Fail(
                new Error(
                    "hello.oidc.pending_identity_invalid",
                    "The external sign-in attempt is invalid or expired.",
                    ErrorType.Unauthorized)),
            httpContext);

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult
        InvalidSession(HttpContext httpContext)
        => OperationResultProblemMapper.ToResult(
            OperationResultFactory.Fail(
                new[]
                {
                    new Error(
                        IdentityErrorCodes.RefreshTokenInvalid,
                        "The session is invalid or expired.",
                        ErrorType.Unauthorized),
                }),
            httpContext);

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult
        InvalidCrossDeviceRequest(HttpContext httpContext)
        => OperationResultProblemMapper.ToResult(
            OperationResultFactory.Fail(
                new Error(
                    IdentityErrorCodes.DeviceAuthorizationInvalid,
                    "The device authorization request is invalid or expired.",
                    ErrorType.Unauthorized)),
            httpContext);

    private static HelloOidcLocalSession? TryReadBearerSession(
        HttpContext httpContext)
    {
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null
            || !Guid.TryParse(
                httpContext.User.FindFirstValue("sub")
                    ?? httpContext.User.FindFirstValue(
                        ClaimTypes.NameIdentifier),
                out var userId)
            || userId == Guid.Empty
            || !Guid.TryParse(
                httpContext.User.FindFirstValue(
                    IdentitySessionClaimTypes.SessionId),
                out var sessionId)
            || sessionId == Guid.Empty)
        {
            return null;
        }

        return new HelloOidcLocalSession(
            userId,
            sessionId,
            accessToken);
    }

    private static string? ReadBearerToken(HttpContext httpContext)
    {
        var authorization =
            httpContext.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorization[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static async Task<IResult> ChangeUserNameAsync<TProfile>(
        ChangeUserNameRequest request,
        IHelloIdentityApplication<TProfile> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var changed = await application.ChangeUserNameAsync(
            new HelloChangeUserNameCommand(
                accessToken,
                request.ExpectedVersion,
                request.UserName),
            cancellationToken);
        return changed.IsSuccess
            ? TypedResults.Ok(ToAccountResponse(changed.Value))
            : OperationResultProblemMapper.ToResult(
                changed,
                httpContext);
    }

    private static async Task<IResult> ChangeEmailAsync<TProfile>(
        ChangeEmailRequest request,
        IHelloIdentityApplication<TProfile> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var changed = await application.ChangeEmailAsync(
            new HelloChangeEmailCommand(
                accessToken,
                request.ExpectedVersion,
                request.Email),
            cancellationToken);
        return changed.IsSuccess
            ? TypedResults.Ok(ToAccountResponse(changed.Value))
            : OperationResultProblemMapper.ToResult(
                changed,
                httpContext);
    }

    private static async Task<IResult> ChangePhoneAsync<TProfile>(
        ChangePhoneRequest request,
        IHelloIdentityApplication<TProfile> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var changed = await application.ChangePhoneAsync(
            new HelloChangePhoneCommand(
                accessToken,
                request.ExpectedVersion,
                request.Phone),
            cancellationToken);
        return changed.IsSuccess
            ? TypedResults.Ok(ToAccountResponse(changed.Value))
            : OperationResultProblemMapper.ToResult(
                changed,
                httpContext);
    }

    private static async Task<IResult> ReplaceProfileAsync<TProfile>(
        ReplaceProfileRequest<TProfile> request,
        IHelloIdentityApplication<TProfile> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var changed = await application.ReplaceProfileAsync(
            new HelloReplaceProfileCommand<TProfile>(
                accessToken,
                request.ExpectedVersion,
                request.Profile),
            cancellationToken);
        return changed.IsSuccess
            ? TypedResults.Ok(ToAccountResponse(changed.Value))
            : OperationResultProblemMapper.ToResult(
                changed,
                httpContext);
    }

    private static void ApplySensitiveResponseHeaders(
        HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl =
            "no-store, max-age=0";
        httpContext.Response.Headers.Pragma = "no-cache";
        httpContext.Response.Headers["Referrer-Policy"] =
            "no-referrer";
        httpContext.Response.Headers["X-Robots-Tag"] =
            "noindex, nofollow";
    }

    private static IResult ToStepUpResult(
        OperationResult<HelloStepUpChallenge> result,
        HttpContext httpContext)
        => result.IsSuccess
            ? TypedResults.Ok(
                new StepUpChallengeResponse(
                    result.Value.ChallengeId,
                    result.Value.ExpiresAt,
                    result.Value.DeliveryChannel.ToString()
                        .ToLowerInvariant()))
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);

    private static IResult FinishSessionEndingMutation(
        OperationResult result,
        IHelloSessionCookieManager cookies,
        HttpContext httpContext)
    {
        if (!result.IsSuccess)
        {
            if (result.Errors.Any(error => string.Equals(
                    error.Code,
                    HelloAccountSecurityActionErrorCodes
                        .SessionCleanupRequired,
                    StringComparison.Ordinal)))
            {
                cookies.DeleteSessionCookies(httpContext);
            }

            return OperationResultProblemMapper.ToResult(
                result,
                httpContext);
        }

        cookies.DeleteSessionCookies(httpContext);
        return TypedResults.NoContent();
    }

    private static AccountResponse<TProfile> ToAccountResponse<TProfile>(
        HelloAccount<TProfile> user)
        => new(
            user.Id,
            user.Flags,
            user.UserName,
            user.Email,
            user.EmailConfirmed,
            user.Phone,
            user.PhoneConfirmed,
            user.Profile,
            user.Version,
            user.CreatedAt,
            user.ModifiedAt);

    private static HelloRegistrationConsent CreateRegistrationConsent(
        bool termsOfService,
        bool privacyPolicy,
        TimeProvider timeProvider)
        => new(
            termsOfService,
            privacyPolicy,
            termsOfService || privacyPolicy
                ? timeProvider.GetUtcNow()
                : null);

    private static TotpStateResponse ToTotpStateResponse(
        HelloTotpState state)
        => new(
            state.IsEnabled,
            state.RecoveryCodesRemaining,
            state.EnabledAt);

    private static SessionResponse ToSessionResponse(
        HelloSession session)
        => new(
            session.SessionId,
            session.AccessToken,
            session.AccessTokenExpiresAt,
            session.RefreshTokenExpiresAt);

    private static SessionInfoResponse ToSessionInfoResponse(
        HelloSessionInfo session)
        => new(
            session.SessionId,
            session.ClientName,
            session.DeviceName,
            session.ExpiresAt,
            session.CreatedAt,
            session.LastRefreshedAt);
}
