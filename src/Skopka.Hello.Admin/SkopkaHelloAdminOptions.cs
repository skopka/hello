namespace Skopka.Hello.Admin;

public sealed class SkopkaHelloAdminOptions
{
    private static readonly string[] ReservedApiPrefixes =
    [
        "/auth",
        "/account",
        "/health",
        "/swagger",
        "/openapi",
        "/_content",
        "/signin-skopka-oidc",
    ];

    public string ApiPathPrefix { get; set; } =
        HelloAdminDefaults.DefaultApiPathPrefix;

    public bool RazorUiEnabled { get; set; } = true;

    public string ReadPolicyName { get; set; } =
        HelloAdminDefaults.ReadPolicy;

    public string ManagePolicyName { get; set; } =
        HelloAdminDefaults.ManagePolicy;

    public string DeletePolicyName { get; set; } =
        HelloAdminDefaults.DeletePolicy;

    public string RoleAssignmentPolicyName { get; set; } =
        HelloAdminDefaults.RoleAssignmentPolicy;

    public string ReadRoleName { get; set; } =
        HelloAdminDefaults.AdministratorRole;

    public string ManageRoleName { get; set; } =
        HelloAdminDefaults.AdministratorRole;

    public string DeleteRoleName { get; set; } =
        HelloAdminDefaults.AdministratorRole;

    public string[] ProtectedRoleNames { get; set; } = [];

    public HelloAdminRoleRulesOptions Roles { get; } = new();

    public HelloAdminRoleAssignmentOptions RoleAssignment { get; } = new();

    public bool RoleManagementEnabled { get; set; } = true;

    public bool RevokeSessionsOnRoleGrant { get; set; } = true;

    public HelloSessionRevocationScope RevokeSessionsOnRoleRemoval
    { get; set; } = HelloSessionRevocationScope.Always;

    public void Validate()
    {
        ApiPathPrefix = ValidatePathPrefix(
            ApiPathPrefix,
            nameof(ApiPathPrefix));
        if (ReservedApiPrefixes.Any(prefix =>
                string.Equals(
                    ApiPathPrefix,
                    prefix,
                    StringComparison.OrdinalIgnoreCase)
                || ApiPathPrefix.StartsWith(
                    prefix + "/",
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "The admin API prefix uses a reserved route namespace.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(ReadPolicyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ManagePolicyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(DeletePolicyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            RoleAssignmentPolicyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ReadRoleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ManageRoleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(DeleteRoleName);
        ArgumentNullException.ThrowIfNull(ProtectedRoleNames);
        ProtectedRoleNames = ValidateRoleNames(
            ProtectedRoleNames,
            nameof(ProtectedRoleNames));
        Roles.Validate();
        RoleAssignment.Validate();
        if (!Enum.IsDefined(RevokeSessionsOnRoleRemoval))
        {
            throw new InvalidOperationException(
                "The role-removal session revocation scope is invalid.");
        }

        if (new[]
            {
                ReadPolicyName,
                ManagePolicyName,
                DeletePolicyName,
                RoleAssignmentPolicyName,
            }.Distinct(StringComparer.Ordinal).Count() != 4)
        {
            throw new InvalidOperationException(
                "Admin read, manage, delete and role-assignment policies must have distinct names.");
        }
    }

    private static string ValidatePathPrefix(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 256
            || value == "/"
            || !value.StartsWith('/')
            || value.EndsWith('/')
            || value.Contains("//", StringComparison.Ordinal)
            || value.Contains("/./", StringComparison.Ordinal)
            || value.Contains("/../", StringComparison.Ordinal)
            || value.EndsWith("/.", StringComparison.Ordinal)
            || value.EndsWith("/..", StringComparison.Ordinal)
            || value.IndexOfAny(
                ['?', '#', '\\', '{', '}', '*', '%']) >= 0
            || value.Any(character =>
                char.IsWhiteSpace(character)
                || char.IsControl(character)))
        {
            throw new InvalidOperationException(
                "The admin API prefix must be a literal absolute path of at most 256 characters, without a trailing slash, empty or dot segments, route parameters, escaping, whitespace, a query or a fragment.");
        }

        return value;
    }

    internal static string[] ValidateRoleNames(
        string[] roleNames,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(roleNames, parameterName);
        if (roleNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                $"{parameterName} cannot contain an empty role name.");
        }

        return roleNames
            .Select(roleName => roleName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public enum HelloSessionRevocationScope
{
    Always = 0,
    ProtectedOnly = 1,
    Never = 2,
}

public enum HelloRoleProtection
{
    Structural = 0,
    Retained = 1,
    System = 2,
}

public sealed class HelloAdminRoleRulesOptions
{
    private readonly Dictionary<string, HelloRoleProtection> protections =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> grantableBy =
        new(StringComparer.OrdinalIgnoreCase);

    public void Protect(
        string roleName,
        HelloRoleProtection protection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        if (!Enum.IsDefined(protection))
        {
            throw new ArgumentOutOfRangeException(nameof(protection));
        }

        protections[roleName.Trim()] = protection;
    }

    public void GrantableBy(
        string roleName,
        IEnumerable<string> roleNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        ArgumentNullException.ThrowIfNull(roleNames);
        grantableBy[roleName.Trim()] =
            SkopkaHelloAdminOptions.ValidateRoleNames(
                roleNames.ToArray(),
                nameof(roleNames));
    }

    internal HelloRoleProtection? FindProtection(string roleName)
        => protections.TryGetValue(roleName.Trim(), out var protection)
            ? protection
            : null;

    internal IReadOnlyList<string> GetGrantableBy(string roleName)
        => grantableBy.TryGetValue(roleName.Trim(), out var roleNames)
            ? roleNames
            : [];

    internal void Validate()
    {
        foreach (var protection in protections.Values)
        {
            if (!Enum.IsDefined(protection))
            {
                throw new InvalidOperationException(
                    "A configured role protection level is invalid.");
            }
        }
    }
}

public sealed class HelloAdminRoleAssignmentOptions
{
    public string? RoleName { get; set; }

    public string[] Assignable { get; set; } = [];

    public string[] NotAssignable { get; set; } = [];

    internal void Validate()
    {
        Assignable = SkopkaHelloAdminOptions.ValidateRoleNames(
            Assignable,
            nameof(Assignable));
        NotAssignable = SkopkaHelloAdminOptions.ValidateRoleNames(
            NotAssignable,
            nameof(NotAssignable));
        if (Assignable.Length > 0 && NotAssignable.Length > 0)
        {
            throw new InvalidOperationException(
                "RoleAssignment.Assignable and RoleAssignment.NotAssignable cannot both be configured.");
        }

        if (string.IsNullOrWhiteSpace(RoleName))
        {
            if (Assignable.Length > 0 || NotAssignable.Length > 0)
            {
                throw new InvalidOperationException(
                    "RoleAssignment.RoleName is required when an assignable-role filter is configured.");
            }

            RoleName = null;
            return;
        }

        RoleName = RoleName.Trim();
    }
}
