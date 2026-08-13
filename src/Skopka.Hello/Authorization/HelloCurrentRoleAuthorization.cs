using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Skopka.Identity.Roles;

namespace Skopka.Hello;

internal sealed record HelloCurrentRoleRequirement(string RoleName)
    : IAuthorizationRequirement;

internal sealed class HelloCurrentRoleHandler<TProfile>(
    IIdentityRoleService<TProfile> roles)
    : AuthorizationHandler<HelloCurrentRoleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        HelloCurrentRoleRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (context.User.Identity?.IsAuthenticated != true
            || !Guid.TryParse(
                context.User.FindFirstValue("sub")
                    ?? context.User.FindFirstValue(
                        ClaimTypes.NameIdentifier),
                out var userId))
        {
            return;
        }

        var cancellationToken = context.Resource is HttpContext httpContext
            ? httpContext.RequestAborted
            : CancellationToken.None;
        var role = await roles.FindByNameAsync(
            requirement.RoleName,
            cancellationToken);
        if (role is null)
        {
            return;
        }

        var membership = await roles.IsUserInRoleAsync(
            userId,
            role.Id,
            cancellationToken);
        if (membership.IsSuccess && membership.Value)
        {
            context.Succeed(requirement);
        }
    }
}
