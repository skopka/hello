using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.UI;

public sealed record HelloUiRegisterCommand(
    string? UserName,
    string? Email,
    string? Phone,
    string DisplayName,
    string? Locale,
    string Password);

public sealed record HelloUiLoginCommand(
    string Handle,
    string Login,
    string Password);

public sealed record HelloUiSignIn(
    ClaimsPrincipal Principal,
    HelloSession Session);

public interface IHelloUiApplication
{
    Task<OperationResult> RegisterAsync(
        HelloUiRegisterCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloUiSignIn>> LoginAsync(
        HelloUiLoginCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<HelloSessionInfo>>> ListSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<OperationResult> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<OperationResult> LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task<OperationResult> LogoutAllAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
