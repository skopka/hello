using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello;
using Skopka.Hello.Oidc;
using Skopka.Identity.Errors;

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

        var oidcEnabled = endpoints.ServiceProvider.GetService<
            IHelloOidcProviderCatalog>() is not null;
        if (oidcEnabled)
        {
            endpoints.MapGet(
                    "/auth/external/providers",
                    GetExternalProviders)
                .AllowAnonymous()
                .WithName("SkopkaHelloGetExternalProviders");
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
        }

        endpoints.MapDelete(
                "/account/sessions/{sessionId:guid}",
                DeleteSessionAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloDeleteSession");

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
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await application.RegisterAsync(
            new HelloRegisterCommand<TProfile>(
                request.UserName,
                request.Email,
                request.Phone,
                request.Profile,
                request.Password),
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

    private static Microsoft.AspNetCore.Http.HttpResults.Ok<
        ExternalProviderResponse[]> GetExternalProviders(
        IHelloOidcProviderCatalog providers)
        => TypedResults.Ok(
            providers.Providers
                .Select(provider => new ExternalProviderResponse(
                    provider.Id,
                    provider.DisplayName))
                .ToArray());

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
                        provider.LinkedAt))
                .ToArray());
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

    private static async Task<IResult>
        CompletePasswordChangeAsync<TProfile>(
            ChangePasswordRequest request,
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

        var result = await application.CompletePasswordChangeAsync(
            new HelloCompletePasswordChangeCommand(
                accessToken,
                request.ChallengeId,
                request.VerificationCode,
                request.CurrentPassword,
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
                request.NewPassword),
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
                request.VerificationCode),
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
                request.VerificationCode),
            cancellationToken);
        return FinishSessionEndingMutation(
            result,
            cookies,
            httpContext);
    }

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
