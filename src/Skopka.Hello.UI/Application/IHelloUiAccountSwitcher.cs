using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.UI;

public sealed record HelloUiSavedAccount(
    Guid UserId,
    Guid SessionId,
    string DisplayName,
    string? UserName,
    string? Email,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);

public interface IHelloUiAccountSwitcher
{
    IReadOnlyList<HelloUiSavedAccount> List(HttpContext httpContext);

    void Save(
        HttpContext httpContext,
        ClaimsPrincipal principal,
        HelloSession session);

    Task<OperationResult<HelloUiSignIn>> SwitchAsync(
        HttpContext httpContext,
        Guid userId,
        CancellationToken cancellationToken);

    Task<OperationResult> RemoveAsync(
        HttpContext httpContext,
        Guid userId,
        bool revokeSession,
        CancellationToken cancellationToken);

    void RemoveSession(HttpContext httpContext, Guid sessionId);
}
