using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Endpoints;
using Skopka.Identity.Errors;
using Skopka.Identity.Roles.Queries;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Queries;

namespace Skopka.Hello.Admin;

public static class SkopkaHelloAdminEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSkopkaHelloAdmin<TProfile>(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider
            .GetRequiredService<SkopkaHelloAdminOptions>();
        var userQueryPath = options.ApiPathPrefix + "/users";
        var roleQueryPath = options.ApiPathPrefix + "/roles";
        if (HasRouteCollision(endpoints, userQueryPath)
            || HasRouteCollision(endpoints, roleQueryPath))
        {
            throw new InvalidOperationException(
                "An admin API query route collides with an existing endpoint.");
        }

        var group = endpoints
            .MapGroup(options.ApiPathPrefix)
            .RequireAuthorization()
            .WithTags("Skopka.Hello Admin");

        group.MapGet("/users", QueryUsersAsync<TProfile>)
            .WithName("SkopkaHelloAdminQueryUsers");
        group.MapPost(
                "/users/{userId:guid}/actions/{action}/challenge",
                BeginActionAsync<TProfile>)
            .WithName("SkopkaHelloAdminBeginUserAction");
        group.MapPost(
                "/users/{userId:guid}/actions/{action}",
                CompleteActionAsync<TProfile>)
            .WithName("SkopkaHelloAdminCompleteUserAction");
        group.MapGet("/roles", QueryRolesAsync<TProfile>)
            .WithName("SkopkaHelloAdminQueryRoles");
        group.MapGet(
                "/users/{userId:guid}/roles",
                GetUserRolesAsync<TProfile>)
            .WithName("SkopkaHelloAdminGetUserRoles");
        group.MapPost(
                "/roles/actions/{action}/challenge",
                BeginRoleActionAsync<TProfile>)
            .WithName("SkopkaHelloAdminBeginRoleAction");
        group.MapPost(
                "/roles/actions/{action}",
                CompleteRoleActionAsync<TProfile>)
            .WithName("SkopkaHelloAdminCompleteRoleAction");

        return endpoints;
    }

    private static bool HasRouteCollision(
        IEndpointRouteBuilder endpoints,
        string path)
        => endpoints.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Any(endpoint => string.Equals(
                "/" + (endpoint.RoutePattern.RawText ?? string.Empty)
                    .Trim('/'),
                path,
                StringComparison.OrdinalIgnoreCase));

    private static async Task<IResult> QueryUsersAsync<TProfile>(
        [FromQuery] string? search,
        [FromQuery] IdentityUserStatus? status,
        [FromQuery] UserFlags? requiredFlags,
        [FromQuery] int? pageSize,
        [FromQuery] DateTimeOffset? cursorCreatedAt,
        [FromQuery] Guid? cursorId,
        IHelloAdminApplication application,
        IAuthorizationService authorization,
        SkopkaHelloAdminOptions options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        if (!await IsAuthorizedAsync(
                authorization,
                httpContext,
                options.ReadPolicyName))
        {
            return TypedResults.Forbid();
        }

        if ((cursorCreatedAt is null) != (cursorId is null))
        {
            return Invalid(
                httpContext,
                "cursor",
                "CursorCreatedAt and CursorId must be supplied together.");
        }

        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.QueryUsersAsync(
            new HelloAdminQueryUsersCommand(
                accessToken,
                search,
                status ?? IdentityUserStatus.Any,
                requiredFlags ?? UserFlags.None,
                pageSize ?? IdentityUserQueryLimits.DefaultPageSize,
                cursorCreatedAt is not null
                    ? new IdentityUserCursor(
                        cursorCreatedAt.Value,
                        cursorId!.Value)
                    : null),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);
    }

    private static async Task<IResult> BeginActionAsync<TProfile>(
        Guid userId,
        string action,
        HelloAdminBeginActionRequest request,
        IHelloAdminApplication application,
        IHelloRequestContext requestContext,
        IAuthorizationService authorization,
        SkopkaHelloAdminOptions options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        if (!HelloAdminSecurity.TryParseActionSlug(
                action,
                out var parsedAction))
        {
            return Invalid(
                httpContext,
                "action",
                "The admin action is invalid.");
        }

        var policy = GetPolicy(options, parsedAction);
        if (!await IsAuthorizedAsync(
                authorization,
                httpContext,
                policy))
        {
            return TypedResults.Forbid();
        }

        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.BeginUserActionAsync(
            new HelloAdminBeginUserActionCommand(
                accessToken,
                userId,
                parsedAction,
                new HelloAdminUserActionParameters(
                    request.ExpectedVersion,
                    request.BlockedUntil,
                    request.Reason),
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(
                new StepUpChallengeResponse(
                    result.Value.ChallengeId,
                    result.Value.ExpiresAt,
                    result.Value.DeliveryChannel.ToString()))
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);
    }

    private static async Task<IResult> QueryRolesAsync<TProfile>(
        [FromQuery] string? search,
        [FromQuery] int? pageSize,
        [FromQuery] DateTimeOffset? cursorCreatedAt,
        [FromQuery] Guid? cursorId,
        IHelloAdminRoleApplication application,
        IAuthorizationService authorization,
        SkopkaHelloAdminOptions options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        if (!await IsAuthorizedAsync(
                authorization,
                httpContext,
                options.ReadPolicyName))
        {
            return TypedResults.Forbid();
        }

        if ((cursorCreatedAt is null) != (cursorId is null))
        {
            return Invalid(
                httpContext,
                "cursor",
                "CursorCreatedAt and CursorId must be supplied together.");
        }

        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.QueryRolesAsync(
            new HelloAdminQueryRolesCommand(
                accessToken,
                search,
                pageSize ?? IdentityRoleQueryLimits.DefaultPageSize,
                cursorCreatedAt is not null
                    ? new IdentityRoleCursor(
                        cursorCreatedAt.Value,
                        cursorId!.Value)
                    : null),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);
    }

    private static async Task<IResult> GetUserRolesAsync<TProfile>(
        Guid userId,
        IHelloAdminRoleApplication application,
        IAuthorizationService authorization,
        SkopkaHelloAdminOptions options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        if (!await IsAuthorizedAsync(
                authorization,
                httpContext,
                options.ReadPolicyName))
        {
            return TypedResults.Forbid();
        }

        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.GetUserRolesAsync(
            new HelloAdminGetUserRolesCommand(accessToken, userId),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);
    }

    private static async Task<IResult> BeginRoleActionAsync<TProfile>(
        string action,
        HelloAdminBeginRoleActionRequest request,
        IHelloAdminRoleApplication application,
        IHelloRequestContext requestContext,
        IAuthorizationService authorization,
        SkopkaHelloAdminOptions options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        if (!HelloAdminSecurity.TryParseRoleActionSlug(
                action,
                out var parsedAction))
        {
            return Invalid(
                httpContext,
                "action",
                "The role action is invalid.");
        }

        if (!await IsAuthorizedAsync(
                authorization,
                httpContext,
                options.DeletePolicyName))
        {
            return TypedResults.Forbid();
        }

        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.BeginRoleActionAsync(
            new HelloAdminBeginRoleActionCommand(
                accessToken,
                parsedAction,
                request.RoleId,
                request.TargetUserId,
                new HelloAdminRoleActionParameters(
                    request.ExpectedVersion,
                    request.Name,
                    request.Description,
                    request.ParentId),
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(
                new StepUpChallengeResponse(
                    result.Value.ChallengeId,
                    result.Value.ExpiresAt,
                    result.Value.DeliveryChannel.ToString()))
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);
    }

    private static async Task<IResult> CompleteRoleActionAsync<TProfile>(
        string action,
        HelloAdminCompleteRoleActionRequest request,
        IHelloAdminRoleApplication application,
        IAuthorizationService authorization,
        SkopkaHelloAdminOptions options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        if (!HelloAdminSecurity.TryParseRoleActionSlug(
                action,
                out var parsedAction))
        {
            return Invalid(
                httpContext,
                "action",
                "The role action is invalid.");
        }

        if (!await IsAuthorizedAsync(
                authorization,
                httpContext,
                options.DeletePolicyName))
        {
            return TypedResults.Forbid();
        }

        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.CompleteRoleActionAsync(
            new HelloAdminCompleteRoleActionCommand(
                accessToken,
                parsedAction,
                request.RoleId,
                request.TargetUserId,
                new HelloAdminRoleActionParameters(
                    request.ExpectedVersion,
                    request.Name,
                    request.Description,
                    request.ParentId),
                request.ChallengeId,
                request.VerificationCode),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);
    }

    private static async Task<IResult> CompleteActionAsync<TProfile>(
        Guid userId,
        string action,
        HelloAdminCompleteActionRequest request,
        IHelloAdminApplication application,
        IAuthorizationService authorization,
        SkopkaHelloAdminOptions options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders(httpContext);
        if (!HelloAdminSecurity.TryParseActionSlug(
                action,
                out var parsedAction))
        {
            return Invalid(
                httpContext,
                "action",
                "The admin action is invalid.");
        }

        var policy = GetPolicy(options, parsedAction);
        if (!await IsAuthorizedAsync(
                authorization,
                httpContext,
                policy))
        {
            return TypedResults.Forbid();
        }

        var accessToken = ReadBearerToken(httpContext);
        if (accessToken is null)
        {
            return InvalidSession(httpContext);
        }

        var result = await application.CompleteUserActionAsync(
            new HelloAdminCompleteUserActionCommand(
                accessToken,
                userId,
                parsedAction,
                new HelloAdminUserActionParameters(
                    request.ExpectedVersion,
                    request.BlockedUntil,
                    request.Reason),
                request.ChallengeId,
                request.VerificationCode),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : OperationResultProblemMapper.ToResult(
                result,
                httpContext);
    }

    private static string GetPolicy(
        SkopkaHelloAdminOptions options,
        HelloAdminUserAction action)
        => action == HelloAdminUserAction.Delete
            ? options.DeletePolicyName
            : options.ManagePolicyName;

    private static async Task<bool> IsAuthorizedAsync(
        IAuthorizationService authorization,
        HttpContext httpContext,
        string policyName)
        => (await authorization.AuthorizeAsync(
            httpContext.User,
            httpContext,
            policyName)).Succeeded;

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

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult
        InvalidSession(HttpContext httpContext)
        => OperationResultProblemMapper.ToResult(
            OperationResultFactory.Fail(
                new Error(
                    IdentityErrorCodes.RefreshTokenInvalid,
                    "The session is invalid or expired.",
                    ErrorType.Unauthorized)),
            httpContext);

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult
        Invalid(
        HttpContext httpContext,
        string field,
        string message)
        => OperationResultProblemMapper.ToResult(
            OperationResultFactory.Fail(
                new Error(
                    IdentityErrorCodes.Validation,
                    "Validation failed.",
                    ErrorType.Validation,
                    new ValidationDetails(
                        new Dictionary<string, string[]>
                        {
                            [field] = [message],
                        }))),
            httpContext);

    private static void ApplySensitiveResponseHeaders(
        HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
        httpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
