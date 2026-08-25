using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Skopka.Identity.Roles;

namespace Skopka.Hello.Admin;

internal sealed record HelloAdminRoleAssignmentRequirement
    : IAuthorizationRequirement;

internal sealed class HelloAdminRoleAssignmentHandler<TProfile>(
    IIdentityRoleService<TProfile> roles,
    HelloAdminRoleRulesEvaluator rules)
    : AuthorizationHandler<HelloAdminRoleAssignmentRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        HelloAdminRoleAssignmentRequirement requirement)
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
        var actorRoles = await roles.GetUserRolesAsync(
            userId,
            cancellationToken);
        if (actorRoles.IsSuccess
            && rules.HasRoleAssignmentCapability(actorRoles.Value))
        {
            context.Succeed(requirement);
        }
    }
}
