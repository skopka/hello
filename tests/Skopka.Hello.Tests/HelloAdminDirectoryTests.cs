using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Admin;
using Skopka.Identity.Roles;
using Skopka.Identity.Roles.Queries;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Queries;

namespace Skopka.Hello.Tests;

public sealed class HelloAdminDirectoryTests
{
    private static readonly IdentityRole Admin = Role("iq-admin");
    private static readonly IdentityRole Teacher = Role("iq-teacher");

    [Fact]
    public async Task BadgesComeFromRolesAndTheHostSource()
    {
        var users = Users(4);
        var roles = new Dictionary<Guid, IReadOnlyList<IdentityRole>>
        {
            [users[0].Id] = [Admin],
            [users[1].Id] = [Teacher],
        };
        var options = Options();
        var source = new Source(users[2].Id);
        var directory = new HelloAdminUserDirectory(
            new FakeUsers(users), new FakeRoles(roles), options, new Services(source));

        var page = await directory.QueryAsync(
            new HelloAdminDirectoryQuery("token", null, IdentityUserStatus.Any, null, 25),
            CancellationToken.None);

        Assert.True(page.IsSuccess);
        Assert.Equal(["admin", "staff"], Keys(page.Value.Rows[0]));
        Assert.Equal(["staff"], Keys(page.Value.Rows[1]));
        Assert.Equal(["staff"], Keys(page.Value.Rows[2]));
        Assert.Empty(page.Value.Rows[3].Badges);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task BadgeFilterPagesThroughAccountsWithACursor()
    {
        var users = Users(230);
        var roles = users.Where((_, index) => index % 10 == 0)
            .ToDictionary(user => user.Id, _ => (IReadOnlyList<IdentityRole>)[Admin]);
        var directory = new HelloAdminUserDirectory(
            new FakeUsers(users), new FakeRoles(roles), Options(), new Services(null));

        var first = await directory.QueryAsync(
            new HelloAdminDirectoryQuery("token", null, IdentityUserStatus.Any, null, 5, Badge: "admin"),
            CancellationToken.None);
        var second = await directory.QueryAsync(
            new HelloAdminDirectoryQuery("token", null, IdentityUserStatus.Any, first.Value.NextCursor, 50, Badge: "admin"),
            CancellationToken.None);

        Assert.Equal(users.Where((_, index) => index % 10 == 0).Take(5).Select(user => user.Id), first.Value.Rows.Select(row => row.User.Id));
        Assert.NotNull(first.Value.NextCursor);
        Assert.Equal(18, second.Value.Rows.Count);
        Assert.Null(second.Value.NextCursor);
        Assert.False(second.Value.LimitReached);
    }

    [Fact]
    public async Task MembershipScanStopsAtTheBoundAndOffersToContinue()
    {
        var users = Users(350);
        var member = users[^1];
        var roles = new Dictionary<Guid, IReadOnlyList<IdentityRole>> { [member.Id] = [Teacher] };
        var options = Options();
        options.MembershipScanLimit = 100;
        var directory = new HelloAdminUserDirectory(
            new FakeUsers(users), new FakeRoles(roles), options, new Services(null));

        var page = await directory.QueryAsync(
            new HelloAdminDirectoryQuery("token", null, IdentityUserStatus.Any, null, 25, MemberOfRoleId: Teacher.Id),
            CancellationToken.None);

        Assert.Empty(page.Value.Rows);
        Assert.True(page.Value.LimitReached);
        Assert.Equal(100, page.Value.Examined);
        Assert.NotNull(page.Value.NextCursor);

        var cursor = page.Value.NextCursor;
        IReadOnlyList<HelloAdminDirectoryRow> found = [];
        while (cursor is not null && found.Count == 0)
        {
            var next = await directory.QueryAsync(
                new HelloAdminDirectoryQuery("token", null, IdentityUserStatus.Any, cursor, 25, MemberOfRoleId: Teacher.Id),
                CancellationToken.None);
            found = next.Value.Rows;
            cursor = next.Value.NextCursor;
        }

        Assert.Equal(member.Id, Assert.Single(found).User.Id);
    }

    [Fact]
    public async Task CandidatesExcludeMembersAndDeletedAccounts()
    {
        var users = Users(3);
        users[2] = users[2] with { DeletedAt = DateTimeOffset.UtcNow };
        var roles = new Dictionary<Guid, IReadOnlyList<IdentityRole>> { [users[0].Id] = [Teacher] };
        var directory = new HelloAdminUserDirectory(
            new FakeUsers(users), new FakeRoles(roles), Options(), new Services(null));

        var page = await directory.QueryAsync(
            new HelloAdminDirectoryQuery("token", null, IdentityUserStatus.Any, null, 10, NotMemberOfRoleId: Teacher.Id),
            CancellationToken.None);

        Assert.Equal(users[1].Id, Assert.Single(page.Value.Rows).User.Id);
    }

    [Fact]
    public void BadgeDeclarationsAreValidated()
    {
        var duplicate = new SkopkaHelloAdminOptions();
        duplicate.UserBadges.Add(HelloAdminUserBadge.ForRoles("admin", "Admin", "a"));
        duplicate.UserBadges.Add(HelloAdminUserBadge.ForRoles("admin", "Other", "b"));
        Assert.Throws<InvalidOperationException>(duplicate.Validate);

        var invalidKey = new SkopkaHelloAdminOptions();
        invalidKey.UserBadges.Add(new HelloAdminUserBadge("Admin Badge", "Admin"));
        Assert.Throws<InvalidOperationException>(invalidKey.Validate);

        var invalidCss = new SkopkaHelloAdminOptions();
        invalidCss.UserBadges.Add(new HelloAdminUserBadge("admin", "Admin") { CssClass = "x\" onclick=\"y" });
        Assert.Throws<InvalidOperationException>(invalidCss.Validate);

        var limit = new SkopkaHelloAdminOptions { MembershipScanLimit = 50 };
        Assert.Throws<InvalidOperationException>(limit.Validate);

        Options().Validate();
    }

    [Fact]
    public async Task DuplicateConfirmationsShareOneCompletion()
    {
        using var guard = new HelloAdminCompletionGuard();
        var actor = Guid.NewGuid();
        var challenge = Guid.NewGuid();
        var calls = 0;
        var release = new TaskCompletionSource();

        async Task<HelloAdminCompletionOutcome> Complete()
        {
            Interlocked.Increment(ref calls);
            await release.Task;
            return new HelloAdminCompletionOutcome(true, "Assign role completed.", []);
        }

        var first = guard.RunAsync(actor, challenge, "role", Complete);
        var second = guard.RunAsync(actor, challenge, "role", Complete);
        release.SetResult();
        var outcomes = await Task.WhenAll(first, second);
        var late = await guard.RunAsync(actor, challenge, "role", Complete);

        Assert.Equal(1, calls);
        Assert.All(outcomes.Append(late), outcome =>
        {
            Assert.True(outcome.Completed);
            Assert.Equal("Assign role completed.", outcome.StatusMessage);
        });
    }

    [Fact]
    public async Task AFailedConfirmationCanBeRetried()
    {
        using var guard = new HelloAdminCompletionGuard();
        var actor = Guid.NewGuid();
        var challenge = Guid.NewGuid();
        var calls = 0;

        Task<HelloAdminCompletionOutcome> Complete()
            => Task.FromResult(++calls == 1
                ? new HelloAdminCompletionOutcome(false, null, [new("", "wrong code")], KeepPending: true)
                : new HelloAdminCompletionOutcome(true, "done", []));

        Assert.False((await guard.RunAsync(actor, challenge, "user", Complete)).Completed);
        Assert.True((await guard.RunAsync(actor, challenge, "user", Complete)).Completed);
        // Another actor never receives this actor's outcome.
        Assert.False((await guard.RunAsync(Guid.NewGuid(), challenge, "user", () =>
            Task.FromResult(new HelloAdminCompletionOutcome(false, null, [])))).Completed);
    }

    private static string[] Keys(HelloAdminDirectoryRow row)
        => row.Badges.Select(badge => badge.Key).ToArray();

    private static SkopkaHelloAdminOptions Options()
    {
        var options = new SkopkaHelloAdminOptions();
        options.UserBadges.Add(HelloAdminUserBadge.ForRoles("admin", "Администратор", " IQ-ADMIN "));
        options.UserBadges.Add(new HelloAdminUserBadge("staff", "Сотрудник")
        {
            Roles = ["iq-admin", "iq-manager", "iq-teacher", "iq-author"],
        });
        return options;
    }

    private static IdentityRole Role(string name)
        => new(Guid.NewGuid(), name, null, null, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static List<HelloAdminUser> Users(int count)
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return Enumerable.Range(0, count)
            .Select(index => new HelloAdminUser(
                Guid.NewGuid(),
                UserFlags.None,
                $"user-{index:000}",
                null,
                false,
                null,
                false,
                [],
                1,
                null,
                null,
                null,
                start.AddMinutes(-index),
                start))
            .ToList();
    }

    private sealed class Source(Guid staff) : IHelloAdminUserBadgeSource
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<Guid, IReadOnlyCollection<string>>> GetBadgesAsync(
            IReadOnlyList<HelloAdminUserBadgeCandidate> users,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyCollection<string>>>(
                new Dictionary<Guid, IReadOnlyCollection<string>> { [staff] = ["staff", "unknown"] });
        }
    }

    private sealed class Services(IHelloAdminUserBadgeSource? source) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IHelloAdminUserBadgeSource) ? source : null;
    }

    private sealed class FakeUsers(IReadOnlyList<HelloAdminUser> users) : IHelloAdminApplication
    {
        public Task<OperationResult<HelloAdminUserPage>> QueryUsersAsync(
            HelloAdminQueryUsersCommand command,
            CancellationToken cancellationToken)
        {
            // Newest first, as the Identity user query orders them.
            var remaining = users
                .Where(user => command.Cursor is null
                    || user.CreatedAt < command.Cursor.CreatedAt
                    || user.CreatedAt == command.Cursor.CreatedAt && user.Id.CompareTo(command.Cursor.Id) < 0)
                .ToArray();
            var items = remaining.Take(command.PageSize).ToArray();
            var last = items.LastOrDefault();
            return Task.FromResult(OperationResultFactory.Success(new HelloAdminUserPage(
                items,
                remaining.Length > command.PageSize && last is not null
                    ? new IdentityUserCursor(last.CreatedAt, last.Id)
                    : null)));
        }

        public Task<OperationResult<HelloStepUpChallenge>> BeginUserActionAsync(
            HelloAdminBeginUserActionCommand command,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult<HelloAdminUserActionResult>> CompleteUserActionAsync(
            HelloAdminCompleteUserActionCommand command,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FakeRoles(IReadOnlyDictionary<Guid, IReadOnlyList<IdentityRole>> roles)
        : IHelloAdminRoleApplication
    {
        public Task<OperationResult<IdentityRolePage>> QueryRolesAsync(
            HelloAdminQueryRolesCommand command,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult<IReadOnlyList<IdentityRole>>> GetUserRolesAsync(
            HelloAdminGetUserRolesCommand command,
            CancellationToken cancellationToken)
            => Task.FromResult(OperationResultFactory.Success(
                roles.TryGetValue(command.TargetUserId, out var assigned) ? assigned : []));

        public Task<OperationResult<HelloStepUpChallenge>> BeginRoleActionAsync(
            HelloAdminBeginRoleActionCommand command,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult<HelloAdminRoleActionResult>> CompleteRoleActionAsync(
            HelloAdminCompleteRoleActionCommand command,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
