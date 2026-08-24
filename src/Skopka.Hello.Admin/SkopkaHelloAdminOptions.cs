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

    public string ReadRoleName { get; set; } =
        HelloAdminDefaults.AdministratorRole;

    public string ManageRoleName { get; set; } =
        HelloAdminDefaults.AdministratorRole;

    public string DeleteRoleName { get; set; } =
        HelloAdminDefaults.AdministratorRole;

    public string[] ProtectedRoleNames { get; set; } = [];

    public bool RoleManagementEnabled { get; set; } = true;

    public bool RevokeSessionsOnRoleGrant { get; set; } = true;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(ReadRoleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ManageRoleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(DeleteRoleName);
        ArgumentNullException.ThrowIfNull(ProtectedRoleNames);

        if (new[]
            {
                ReadPolicyName,
                ManagePolicyName,
                DeletePolicyName,
            }.Distinct(StringComparer.Ordinal).Count() != 3)
        {
            throw new InvalidOperationException(
                "Admin read, manage and delete policies must have distinct names.");
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
}
