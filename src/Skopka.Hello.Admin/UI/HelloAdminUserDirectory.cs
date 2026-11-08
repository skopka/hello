using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Roles;
using Skopka.Identity.Users.Queries;

namespace Skopka.Hello.Admin;

internal sealed record HelloAdminDirectoryQuery(
    string AccessToken,
    string? Search,
    IdentityUserStatus Status,
    IdentityUserCursor? Cursor,
    int PageSize,
    string? Badge = null,
    Guid? MemberOfRoleId = null,
    Guid? NotMemberOfRoleId = null)
{
    public bool Filtered => Badge is not null
        || MemberOfRoleId is not null
        || NotMemberOfRoleId is not null;
}

internal sealed record HelloAdminDirectoryRow(
    HelloAdminUser User,
    IReadOnlyList<IdentityRole> Roles,
    IReadOnlyList<HelloAdminUserBadge> Badges);

/// <param name="LimitReached">
/// The scan stopped at <see cref="SkopkaHelloAdminOptions.MembershipScanLimit"/>
/// before the page filled; <paramref name="NextCursor"/> continues it.
/// </param>
internal sealed record HelloAdminDirectoryPage(
    IReadOnlyList<HelloAdminDirectoryRow> Rows,
    IdentityUserCursor? NextCursor,
    int Examined,
    bool LimitReached);

/// <summary>
/// The Admin user list with roles and badges. Identity has no query for role
/// members or host attributes, so filtered lists page through accounts with
/// the bounded user query and keep the matching ones, examining at most
/// <see cref="SkopkaHelloAdminOptions.MembershipScanLimit"/> accounts per
/// request.
/// </summary>
internal sealed class HelloAdminUserDirectory(
    IHelloAdminApplication users,
    IHelloAdminRoleApplication roles,
    SkopkaHelloAdminOptions options,
    IServiceProvider services)
{
    private const int BatchSize = IdentityUserQueryLimits.MaximumPageSize;

    public async Task<OperationResult<HelloAdminDirectoryPage>> QueryAsync(
        HelloAdminDirectoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        HelloAdminUserBadge? badge = null;
        if (query.Badge is not null)
        {
            badge = options.UserBadges.FirstOrDefault(item =>
                item.Filterable
                && string.Equals(item.Key, query.Badge, StringComparison.Ordinal));
            if (badge is null)
            {
                return OperationResultFactory.Success(
                    new HelloAdminDirectoryPage([], null, 0, false));
            }
        }

        if (!query.Filtered)
        {
            var page = await users.QueryUsersAsync(
                new HelloAdminQueryUsersCommand(
                    query.AccessToken,
                    query.Search,
                    query.Status,
                    PageSize: query.PageSize,
                    Cursor: query.Cursor),
                cancellationToken);
            if (!page.IsSuccess)
            {
                return OperationResultFactory.Fail<HelloAdminDirectoryPage>(page.Errors);
            }

            var rows = await DecorateAsync(query.AccessToken, page.Value.Items, cancellationToken);
            return rows.IsSuccess
                ? OperationResultFactory.Success(new HelloAdminDirectoryPage(
                    rows.Value,
                    page.Value.NextCursor,
                    page.Value.Items.Count,
                    LimitReached: false))
                : OperationResultFactory.Fail<HelloAdminDirectoryPage>(rows.Errors);
        }

        List<HelloAdminDirectoryRow> matches = [];
        var cursor = query.Cursor;
        var examined = 0;
        while (true)
        {
            var batchSize = Math.Min(BatchSize, options.MembershipScanLimit - examined);
            var page = await users.QueryUsersAsync(
                new HelloAdminQueryUsersCommand(
                    query.AccessToken,
                    query.Search,
                    query.Status,
                    PageSize: batchSize,
                    Cursor: cursor),
                cancellationToken);
            if (!page.IsSuccess)
            {
                return OperationResultFactory.Fail<HelloAdminDirectoryPage>(page.Errors);
            }

            var rows = await DecorateAsync(query.AccessToken, page.Value.Items, cancellationToken);
            if (!rows.IsSuccess)
            {
                return OperationResultFactory.Fail<HelloAdminDirectoryPage>(rows.Errors);
            }

            for (var index = 0; index < rows.Value.Count; index++)
            {
                var row = rows.Value[index];
                examined++;
                if (!Matches(row, query, badge))
                {
                    continue;
                }

                matches.Add(row);
                if (matches.Count == query.PageSize)
                {
                    var more = index < rows.Value.Count - 1 || page.Value.NextCursor is not null;
                    return OperationResultFactory.Success(new HelloAdminDirectoryPage(
                        matches,
                        more ? new IdentityUserCursor(row.User.CreatedAt, row.User.Id) : null,
                        examined,
                        LimitReached: false));
                }
            }

            if (page.Value.NextCursor is null)
            {
                return OperationResultFactory.Success(
                    new HelloAdminDirectoryPage(matches, null, examined, false));
            }

            cursor = page.Value.NextCursor;
            if (examined >= options.MembershipScanLimit)
            {
                return OperationResultFactory.Success(
                    new HelloAdminDirectoryPage(matches, cursor, examined, LimitReached: true));
            }
        }
    }

    private static bool Matches(
        HelloAdminDirectoryRow row,
        HelloAdminDirectoryQuery query,
        HelloAdminUserBadge? badge)
        => (badge is null || row.Badges.Contains(badge))
            && (query.MemberOfRoleId is not { } member
                || row.Roles.Any(role => role.Id == member))
            && (query.NotMemberOfRoleId is not { } excluded
                || row.User.DeletedAt is null && row.Roles.All(role => role.Id != excluded));

    private async Task<OperationResult<IReadOnlyList<HelloAdminDirectoryRow>>> DecorateAsync(
        string accessToken,
        IReadOnlyList<HelloAdminUser> page,
        CancellationToken cancellationToken)
    {
        List<(HelloAdminUser User, IReadOnlyList<IdentityRole> Roles)> withRoles = [];
        foreach (var user in page)
        {
            if (user.DeletedAt is not null)
            {
                withRoles.Add((user, []));
                continue;
            }

            var assigned = await roles.GetUserRolesAsync(
                new HelloAdminGetUserRolesCommand(accessToken, user.Id),
                cancellationToken);
            if (!assigned.IsSuccess)
            {
                return OperationResultFactory.Fail<IReadOnlyList<HelloAdminDirectoryRow>>(assigned.Errors);
            }

            withRoles.Add((user, assigned.Value));
        }

        IReadOnlyDictionary<Guid, IReadOnlyCollection<string>> hostBadges =
            new Dictionary<Guid, IReadOnlyCollection<string>>();
        if (options.UserBadges.Count > 0
            && withRoles.Count > 0
            && services.GetService<IHelloAdminUserBadgeSource>() is { } source)
        {
            hostBadges = await source.GetBadgesAsync(
                withRoles.Select(item => new HelloAdminUserBadgeCandidate(
                    item.User.Id,
                    item.User.Flags,
                    item.User.UserName,
                    item.User.Email,
                    item.User.Phone,
                    item.Roles.Select(role => role.Name).ToArray())).ToArray(),
                cancellationToken) ?? hostBadges;
        }

        return OperationResultFactory.Success<IReadOnlyList<HelloAdminDirectoryRow>>(withRoles
            .Select(item => new HelloAdminDirectoryRow(
                item.User,
                item.Roles,
                options.UserBadges
                    .Where(badge => item.Roles.Any(role => badge.MatchesRole(role.Name))
                        || hostBadges.TryGetValue(item.User.Id, out var keys)
                            && keys.Contains(badge.Key, StringComparer.Ordinal))
                    .ToArray()))
            .ToArray());
    }
}
