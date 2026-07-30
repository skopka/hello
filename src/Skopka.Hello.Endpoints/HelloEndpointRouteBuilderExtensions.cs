using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity;
using Skopka.Identity.Authentication;
using Skopka.Identity.Errors;
using Skopka.Identity.Registration;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Commands;

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

        endpoints.MapDelete(
                "/account/sessions/{sessionId:guid}",
                DeleteSessionAsync<TProfile>)
            .RequireAuthorization()
            .WithName("SkopkaHelloDeleteSession");

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync<TProfile>(
        RegisterRequest<TProfile> request,
        IIdentityRegistrationService<TProfile> registration,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await registration.RegisterPasswordAsync(
            new RegisterPasswordUserCommand<TProfile>(
                new CreateUserCommand<TProfile>(
                    request.UserName,
                    request.Email,
                    request.Phone,
                    request.Profile),
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
        IPasswordAuthenticationService<TProfile> authentication,
        IIdentitySessionService<TProfile> sessions,
        Skopka.Hello.IHelloRequestContext requestContext,
        Skopka.Hello.SkopkaHelloOptions options,
        IAntiforgery antiforgery,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var transport = ValidateCookieTransport(
            options,
            httpContext);
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

        var authenticated = await authentication.AuthenticateAsync(
            new AuthenticatePasswordCommand(
                handle,
                request.Login,
                request.Password,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        if (!authenticated.IsSuccess)
        {
            return InvalidLogin(httpContext);
        }

        var issued = await sessions.CreateAsync(
            new CreateIdentitySessionCommand(
                authenticated.Value.Id,
                authenticated.Value.SecurityStamp,
                requestContext.CreateSessionMetadata(
                    httpContext,
                    options.ClientName)),
            cancellationToken);
        if (!issued.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                issued,
                httpContext);
        }

        WriteSessionCookies(
            httpContext,
            antiforgery,
            options,
            issued.Value);
        return TypedResults.Ok(ToSessionResponse(issued.Value));
    }

    private static async Task<IResult> RefreshAsync<TProfile>(
        IIdentitySessionService<TProfile> sessions,
        Skopka.Hello.SkopkaHelloOptions options,
        IAntiforgery antiforgery,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var transport = ValidateCookieTransport(
            options,
            httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        var csrf = await ValidateAntiforgeryAsync(
            antiforgery,
            httpContext);
        if (!csrf.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                csrf,
                httpContext);
        }

        if (!httpContext.Request.Cookies.TryGetValue(
                options.RefreshCookieName,
                out var refreshToken)
            || string.IsNullOrWhiteSpace(refreshToken))
        {
            return InvalidSession(httpContext);
        }

        var refreshed = await sessions.RefreshAsync(
            new RefreshIdentitySessionCommand(refreshToken),
            cancellationToken);
        if (!refreshed.IsSuccess)
        {
            DeleteSessionCookies(httpContext, options);
            return OperationResultProblemMapper.ToResult(
                refreshed,
                httpContext);
        }

        WriteSessionCookies(
            httpContext,
            antiforgery,
            options,
            refreshed.Value);
        return TypedResults.Ok(ToSessionResponse(refreshed.Value));
    }

    private static async Task<IResult> LogoutAsync<TProfile>(
        IIdentitySessionService<TProfile> sessions,
        Skopka.Hello.SkopkaHelloOptions options,
        IAntiforgery antiforgery,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var transport = ValidateCookieTransport(
            options,
            httpContext);
        if (!transport.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                transport,
                httpContext);
        }

        var csrf = await ValidateAntiforgeryAsync(
            antiforgery,
            httpContext);
        if (!csrf.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                csrf,
                httpContext);
        }

        if (!httpContext.Request.Cookies.TryGetValue(
                options.RefreshCookieName,
                out var refreshToken)
            || string.IsNullOrWhiteSpace(refreshToken))
        {
            DeleteSessionCookies(httpContext, options);
            return TypedResults.NoContent();
        }

        var revoked = await sessions.RevokeAsync(
            new RevokeIdentitySessionCommand(refreshToken),
            cancellationToken);
        DeleteSessionCookies(httpContext, options);

        return revoked.IsSuccess
            ? TypedResults.NoContent()
            : OperationResultProblemMapper.ToResult(
                revoked,
                httpContext);
    }

    private static async Task<IResult> LogoutAllAsync<TProfile>(
        IIdentitySessionService<TProfile> sessions,
        Skopka.Hello.SkopkaHelloOptions options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryReadUserId(httpContext.User, out var userId))
        {
            return InvalidSession(httpContext);
        }

        var revoked = await sessions.RevokeAllAsync(
            new RevokeAllIdentitySessionsCommand(userId),
            cancellationToken);
        if (!revoked.IsSuccess)
        {
            return OperationResultProblemMapper.ToResult(
                revoked,
                httpContext);
        }

        DeleteSessionCookies(httpContext, options);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetMeAsync<TProfile>(
        IIdentitySessionService<TProfile> sessions,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var validated = await sessions.ValidateAccessTokenAsync(
            accessToken,
            cancellationToken);
        return validated.IsSuccess
            ? TypedResults.Ok(ToAccountResponse(validated.Value))
            : OperationResultProblemMapper.ToResult(
                validated,
                httpContext);
    }

    private static async Task<IResult> GetSessionsAsync<TProfile>(
        IIdentitySessionService<TProfile> sessions,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryReadUserId(httpContext.User, out var userId))
        {
            return InvalidSession(httpContext);
        }

        var listed = await sessions.ListAsync(
            new ListIdentitySessionsCommand(userId),
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

    private static async Task<IResult> DeleteSessionAsync<TProfile>(
        Guid sessionId,
        IIdentitySessionService<TProfile> sessions,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryReadUserId(httpContext.User, out var userId))
        {
            return InvalidSession(httpContext);
        }

        var revoked = await sessions.RevokeByIdAsync(
            new RevokeIdentitySessionByIdCommand(
                userId,
                sessionId),
            cancellationToken);
        return revoked.IsSuccess
            ? TypedResults.NoContent()
            : OperationResultProblemMapper.ToResult(
                revoked,
                httpContext);
    }

    private static async Task<OperationResult>
        ValidateAntiforgeryAsync(
            IAntiforgery antiforgery,
            HttpContext httpContext)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
            return OperationResultFactory.Success();
        }
        catch (AntiforgeryValidationException)
        {
            return OperationResultFactory.Fail(
                new[]
                {
                    new Error(
                        "hello.csrf.invalid",
                        "The CSRF token is missing or invalid.",
                        ErrorType.Forbidden),
                });
        }
    }

    private static OperationResult ValidateCookieTransport(
        Skopka.Hello.SkopkaHelloOptions options,
        HttpContext httpContext)
        => options.SecureCookies && !httpContext.Request.IsHttps
            ? OperationResultFactory.Fail(
                new[]
                {
                    new Error(
                        "hello.https.required",
                        "HTTPS is required for session cookies.",
                        ErrorType.Forbidden),
                })
            : OperationResultFactory.Success();

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult
        InvalidLogin(HttpContext httpContext)
        => OperationResultProblemMapper.ToResult(
            OperationResultFactory.Fail(
                new[]
                {
                    new Error(
                        IdentityErrorCodes.InvalidCredentials,
                        "The login or password is invalid.",
                        ErrorType.Unauthorized),
                }),
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

    private static void WriteSessionCookies(
        HttpContext httpContext,
        IAntiforgery antiforgery,
        Skopka.Hello.SkopkaHelloOptions options,
        IssuedIdentitySession session)
    {
        httpContext.Response.Cookies.Append(
            options.RefreshCookieName,
            session.RefreshToken,
            CreateCookieOptions(
                options,
                httpOnly: true,
                session.RefreshTokenExpiresAt));

        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        if (string.IsNullOrWhiteSpace(tokens.RequestToken))
        {
            throw new InvalidOperationException(
                "The antiforgery service did not issue a request token.");
        }

        httpContext.Response.Cookies.Append(
            options.AntiforgeryRequestCookieName,
            tokens.RequestToken,
            CreateCookieOptions(
                options,
                httpOnly: false,
                session.RefreshTokenExpiresAt));
    }

    private static void DeleteSessionCookies(
        HttpContext httpContext,
        Skopka.Hello.SkopkaHelloOptions options)
    {
        var cookie = CreateCookieOptions(
            options,
            httpOnly: true,
            expires: null);
        httpContext.Response.Cookies.Delete(
            options.RefreshCookieName,
            cookie);
        httpContext.Response.Cookies.Delete(
            options.AntiforgeryCookieName,
            cookie);
        httpContext.Response.Cookies.Delete(
            options.AntiforgeryRequestCookieName,
            CreateCookieOptions(
                options,
                httpOnly: false,
                expires: null));
    }

    private static CookieOptions CreateCookieOptions(
        Skopka.Hello.SkopkaHelloOptions options,
        bool httpOnly,
        DateTimeOffset? expires)
        => new()
        {
            HttpOnly = httpOnly,
            Secure = options.SecureCookies,
            IsEssential = true,
            SameSite = options.CookieSameSite,
            Path = "/",
            Expires = expires,
        };

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
        return authorization.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase)
            ? authorization[prefix.Length..].Trim()
            : null;
    }

    private static AccountResponse<TProfile> ToAccountResponse<TProfile>(
        IdentityUser<TProfile> user)
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
        IssuedIdentitySession session)
        => new(
            session.SessionId,
            session.AccessToken,
            session.AccessTokenExpiresAt,
            session.RefreshTokenExpiresAt);

    private static SessionInfoResponse ToSessionInfoResponse(
        IdentitySessionInfo session)
        => new(
            session.SessionId,
            session.Metadata.ClientName,
            session.Metadata.DeviceName,
            session.ExpiresAt,
            session.CreatedAt,
            session.LastRefreshedAt);
}
