using System.Text.RegularExpressions;
using Skopka.Identity.Users;

namespace Skopka.Hello.Admin;

/// <summary>
/// A host-declared account attribute shown as a badge in the Admin user list
/// and offered as a list filter, for example "Administrator" or "Staff".
/// </summary>
/// <remarks>
/// Hello does not know what an attribute means for a host. An account holds a
/// badge when it has any of <see cref="Roles"/>, or, for a badge without roles
/// or with <see cref="AlsoFromHost"/>, when the host's
/// <see cref="IHelloAdminUserBadgeSource"/> reports it.
/// </remarks>
public sealed partial class HelloAdminUserBadge
{
    /// <summary>
    /// Creates a badge.
    /// </summary>
    /// <param name="key">
    /// Stable identifier used in the list filter URL and by the badge source:
    /// lowercase Latin letters, digits and hyphens, at most 64 characters.
    /// </param>
    /// <param name="label">
    /// Host-owned text shown on the badge and in the filter. Hello does not
    /// translate it; a UI dictionary key <c>Admin.Badges.{key}</c> overrides it.
    /// </param>
    public HelloAdminUserBadge(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }

    public string Label { get; }

    /// <summary>
    /// Role names, any of which grants the badge. Matching ignores case and
    /// surrounding spaces. Leave empty for a badge decided only by the host's
    /// <see cref="IHelloAdminUserBadgeSource"/>.
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>
    /// For a badge with <see cref="Roles"/>: whether the host's
    /// <see cref="IHelloAdminUserBadgeSource"/> may also grant it. The default is
    /// <see langword="false"/>: the badge means "has one of the roles", and its list
    /// filter is answered by Identity in the database. When <see langword="true"/>,
    /// the filter pages through accounts like a host badge does. A badge without
    /// roles is always decided by the badge source.
    /// </summary>
    public bool AlsoFromHost { get; init; }

    internal bool FromHost => Roles.Count == 0 || AlsoFromHost;

    /// <summary>
    /// Bootstrap badge classes. The default is <c>text-bg-info</c>.
    /// </summary>
    public string CssClass { get; init; } = "text-bg-info";

    /// <summary>Whether the user list offers a filter for this badge. The default is <see langword="true"/>.</summary>
    public bool Filterable { get; init; } = true;

    /// <summary>Creates a badge held by accounts with any of <paramref name="roles"/>.</summary>
    public static HelloAdminUserBadge ForRoles(
        string key,
        string label,
        params string[] roles)
        => new(key, label) { Roles = roles };

    internal bool MatchesRole(string roleName)
        => Roles.Any(role => string.Equals(
            role.Trim(),
            roleName.Trim(),
            StringComparison.OrdinalIgnoreCase));

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key) || !KeyPattern().IsMatch(Key))
        {
            throw new InvalidOperationException(
                "An Admin user badge key must contain 1-64 lowercase Latin letters, digits or hyphens.");
        }

        if (string.IsNullOrWhiteSpace(Label) || Label.Length > 64)
        {
            throw new InvalidOperationException(
                $"The Admin user badge '{Key}' requires a label of at most 64 characters.");
        }

        ArgumentNullException.ThrowIfNull(Roles);
        if (Roles.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                $"The Admin user badge '{Key}' contains an empty role name.");
        }

        if (string.IsNullOrWhiteSpace(CssClass)
            || CssClass.Any(character => !(char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or ' ')))
        {
            throw new InvalidOperationException(
                $"The Admin user badge '{Key}' has invalid CSS classes.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}

/// <summary>What a host may inspect to decide its badges for one account.</summary>
public sealed record HelloAdminUserBadgeCandidate(
    Guid UserId,
    UserFlags Flags,
    string? UserName,
    string? Email,
    string? Phone,
    IReadOnlyList<string> RoleNames);

/// <summary>
/// Optional host service that decides badges Hello cannot derive from roles,
/// for example an account kind the host stores itself. Register it in DI; it
/// is resolved per request.
/// </summary>
public interface IHelloAdminUserBadgeSource
{
    /// <summary>
    /// Returns the declared badge keys each account holds. Accounts without
    /// badges may be omitted. Unknown keys are ignored. Called once per batch
    /// of at most 100 accounts.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyCollection<string>>> GetBadgesAsync(
        IReadOnlyList<HelloAdminUserBadgeCandidate> users,
        CancellationToken cancellationToken);
}
