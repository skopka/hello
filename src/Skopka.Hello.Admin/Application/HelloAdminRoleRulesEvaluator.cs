using Skopka.Identity.Roles;

namespace Skopka.Hello.Admin;

internal sealed class HelloAdminRoleRulesEvaluator(
    SkopkaHelloAdminOptions options)
{
    public HelloRoleProtection? GetProtection(string roleName)
    {
        if (Matches(roleName, options.ReadRoleName)
            || Matches(roleName, options.ManageRoleName)
            || Matches(roleName, options.DeleteRoleName))
        {
            return HelloRoleProtection.System;
        }

        if (options.ProtectedRoleNames.Any(
                protectedRoleName => Matches(
                    roleName,
                    protectedRoleName)))
        {
            return HelloRoleProtection.Retained;
        }

        return options.Roles.FindProtection(roleName);
    }

    public bool HasRoleAssignmentCapability(
        IReadOnlyCollection<IdentityRole> actorRoles)
    {
        var actorRoleNames = ActorRoleNames(actorRoles);
        return actorRoleNames.Contains(options.DeleteRoleName.Trim())
            || options.RoleAssignment.RoleName is { } delegateRoleName
            && actorRoleNames.Contains(delegateRoleName.Trim());
    }

    public bool CanManageMembership(
        IReadOnlyCollection<IdentityRole> actorRoles,
        IdentityRole targetRole)
    {
        var actorRoleNames = ActorRoleNames(actorRoles);
        var grantableBy = options.Roles.GetGrantableBy(targetRole.Name);
        if (grantableBy.Count > 0
            && !grantableBy.Any(actorRoleNames.Contains))
        {
            return false;
        }

        if (actorRoleNames.Contains(options.DeleteRoleName.Trim()))
        {
            return true;
        }

        if (options.RoleAssignment.RoleName is not { } delegateRoleName
            || !actorRoleNames.Contains(delegateRoleName.Trim()))
        {
            return false;
        }

        if (options.RoleAssignment.Assignable.Length > 0)
        {
            return options.RoleAssignment.Assignable.Any(
                roleName => Matches(targetRole.Name, roleName));
        }

        return !options.RoleAssignment.NotAssignable.Any(
            roleName => Matches(targetRole.Name, roleName));
    }

    private static HashSet<string> ActorRoleNames(
        IReadOnlyCollection<IdentityRole> actorRoles)
        => actorRoles
            .Where(role => !string.IsNullOrWhiteSpace(role.Name))
            .Select(role => role.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool Matches(string left, string right)
        => string.Equals(
            left.Trim(),
            right.Trim(),
            StringComparison.OrdinalIgnoreCase);
}
