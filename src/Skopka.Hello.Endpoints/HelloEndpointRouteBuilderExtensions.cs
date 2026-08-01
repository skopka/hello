using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello;
using Skopka.Hello.Oidc;
using Skopka.Identity.Authentication;
using Skopka.Identity.Errors;

namespace Skopka.Hello.Endpoints;

public static class HelloEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSkopkaHello<TProfile>(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
                "/auth/register",
                RegisterAsync<TProfile>)
            .WithName("SkopkaHelloRegister");

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

        endpoints.MapGet(
                "/account/me",
                GetMeAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloGetMe");

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

        if (!TryParseHandle(request.Handle, out var handle))
        {
            return OperationResultProblemMapper.ToResult(
                OperationResultFactory.Fail(
                    new[]
                    {
                        new Error(
                            IdentityErrorCodes.Validation,
                            "Validation failed.",
                            ErrorType.Validation,
                            new ValidationDetails(
                                new Dictionary<string, string[]>
                                {
                                    [nameof(request.Handle)] =
                                    [
                                        "Handle must be 'userName' or 'email'.",
                                    ],
                                })),
                    }),
                httpContext);
        }

        var authenticated = await application.LoginAsync(
            new HelloLoginCommand(
                handle,
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
        if (!TryReadUserId(httpContext.User, out var userId))
        {
            return InvalidSession(httpContext);
        }

        var revoked = await application.LogoutAllAsync(
            userId,
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
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await application.RequestPasswordResetAsync(
            request.Email,
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
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var result = await application.RequestEmailConfirmationAsync(
            request.Email,
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

    private static async Task<IResult> GetMeAsync<TProfile>(
        IHelloIdentityApplication<TProfile> application,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
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
        if (!TryReadUserId(httpContext.User, out var userId))
        {
            return InvalidSession(httpContext);
        }

        var listed = await application.ListSessionsAsync(
            userId,
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

        httpContext.Response.Headers.CacheControl = "no-store";
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
        if (!TryReadUserId(httpContext.User, out var userId))
        {
            return InvalidSession(httpContext);
        }

        var revoked = await application.RevokeSessionAsync(
            userId,
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
                    result.Value.ExpiresAt))
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

    private static bool TryParseHandle(
        string? value,
        out PasswordLoginHandle handle)
    {
        if (string.Equals(
                value,
                "email",
                StringComparison.OrdinalIgnoreCase))
        {
            handle = PasswordLoginHandle.Email;
            return true;
        }

        if (string.Equals(
                value,
                "username",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                value,
                "userName",
                StringComparison.OrdinalIgnoreCase))
        {
            handle = PasswordLoginHandle.UserName;
            return true;
        }

        handle = default;
        return false;
    }

    private static bool TryReadUserId(
        ClaimsPrincipal principal,
        out Guid userId)
    {
        var subject = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(subject, out userId);
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
