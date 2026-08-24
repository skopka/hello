using Skopka.Abstraction.OperationResult;
using Microsoft.AspNetCore.Http;
using Skopka.Hello.Admin;
using Skopka.Identity.Roles;
using Skopka.Identity.Roles.Commands;
using Skopka.Identity.Roles.Queries;
using Skopka.Identity.Sessions;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Users;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Tests;

public sealed class HelloAdminRoleApplicationTests
{
    [Fact]
    public async Task QueryRolesUsesBoundedIdentityServiceAfterOnlineValidation()
    {
        var fixture = new Fixture();
        var cursor = new IdentityRoleCursor(
            DateTimeOffset.UtcNow.AddDays(-1),
            Guid.NewGuid());

        var result = await fixture.Application.QueryRolesAsync(
            new HelloAdminQueryRolesCommand(
                "access-token",
                " operator ",
                17,
                cursor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("access-token", fixture.Sessions.ValidatedAccessToken);
        Assert.NotNull(fixture.RoleQueries.Query);
        Assert.Equal(" operator ", fixture.RoleQueries.Query.Search);
        Assert.Equal(17, fixture.RoleQueries.Query.PageSize);
        Assert.Equal(cursor, fixture.RoleQueries.Query.Cursor);
    }

    [Fact]
    public async Task AssignRoleUsesSameBoundProofAndRevokesTargetSessions()
    {
        var fixture = new Fixture();
        var targetUserId = Guid.NewGuid();

        var begun = await fixture.Application.BeginRoleActionAsync(
            new HelloAdminBeginRoleActionCommand(
                "access-token",
                HelloAdminRoleAction.Assign,
                fixture.Role.Id,
                targetUserId,
                new HelloAdminRoleActionParameters(),
                "client-key"),
            CancellationToken.None);

        Assert.True(begun.IsSuccess);
        Assert.Equal(
            HelloAdminSecurity.AssignRoleAction,
            fixture.StepUp.BeginCommand?.Action);
        Assert.Equal("client-key", fixture.StepUp.BeginCommand?.ClientKey);
        Assert.Equal("123456", fixture.Messages.Message?.VerificationCode);

        var completed = await fixture.Application.CompleteRoleActionAsync(
            new HelloAdminCompleteRoleActionCommand(
                "access-token",
                HelloAdminRoleAction.Assign,
                fixture.Role.Id,
                targetUserId,
                new HelloAdminRoleActionParameters(),
                begun.Value.ChallengeId,
                "123456"),
            CancellationToken.None);

        Assert.True(completed.IsSuccess);
        Assert.Equal(targetUserId, fixture.Roles.Assigned?.UserId);
        Assert.Equal(fixture.Role.Id, fixture.Roles.Assigned?.RoleId);
        Assert.Equal(targetUserId, fixture.Sessions.RevokedUserId);
        Assert.Equal(
            fixture.StepUp.BeginCommand?.Binding,
            fixture.StepUp.AuthorizeCommand?.Binding);
        Assert.True(completed.Value.SessionsRevoked);
    }

    [Fact]
    public async Task ProtectedAdminRoleCannotBeUpdated()
    {
        var fixture = new Fixture(
            roleName: HelloAdminDefaults.AdministratorRole);

        var result = await fixture.Application.BeginRoleActionAsync(
            new HelloAdminBeginRoleActionCommand(
                "access-token",
                HelloAdminRoleAction.Update,
                fixture.Role.Id,
                TargetUserId: null,
                new HelloAdminRoleActionParameters(
                    ExpectedVersion: fixture.Role.Version,
                    Name: "renamed"),
                ClientKey: null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == HelloAdminErrorCodes.ProtectedRoleMutationForbidden);
        Assert.Null(fixture.StepUp.BeginCommand);
    }

    [Theory]
    [InlineData(HelloAdminRoleAction.Update)]
    [InlineData(HelloAdminRoleAction.Delete)]
    public async Task ConfiguredProtectedRoleCannotBeUpdatedOrDeleted(
        HelloAdminRoleAction action)
    {
        var fixture = new Fixture(
            roleName: "IQ-Author",
            configureOptions: options =>
                options.ProtectedRoleNames = [" iq-author "]);
        var parameters = action == HelloAdminRoleAction.Update
            ? new HelloAdminRoleActionParameters(
                ExpectedVersion: fixture.Role.Version,
                Name: "renamed")
            : new HelloAdminRoleActionParameters(
                ExpectedVersion: fixture.Role.Version);

        var result = await fixture.Application.BeginRoleActionAsync(
            new HelloAdminBeginRoleActionCommand(
                "access-token",
                action,
                fixture.Role.Id,
                TargetUserId: null,
                parameters,
                ClientKey: null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == HelloAdminErrorCodes.ProtectedRoleMutationForbidden);
        Assert.Null(fixture.StepUp.BeginCommand);
    }

    [Fact]
    public async Task EmptyProtectedRoleListPreservesRoleUpdate()
    {
        var fixture = new Fixture();

        var result = await fixture.Application.BeginRoleActionAsync(
            new HelloAdminBeginRoleActionCommand(
                "access-token",
                HelloAdminRoleAction.Update,
                fixture.Role.Id,
                TargetUserId: null,
                new HelloAdminRoleActionParameters(
                    ExpectedVersion: fixture.Role.Version,
                    Name: "renamed"),
                ClientKey: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(fixture.StepUp.BeginCommand);
    }

    [Theory]
    [InlineData(HelloAdminRoleAction.Assign)]
    [InlineData(HelloAdminRoleAction.Remove)]
    public async Task ConfiguredProtectedRoleMembershipCanStillBeChanged(
        HelloAdminRoleAction action)
    {
        var fixture = new Fixture(
            roleName: "iq-teacher",
            configureOptions: options =>
            {
                options.ProtectedRoleNames = ["IQ-TEACHER"];
                options.RoleManagementEnabled = false;
            });
        var targetUserId = Guid.NewGuid();
        var parameters = new HelloAdminRoleActionParameters();
        var begun = await fixture.Application.BeginRoleActionAsync(
            new HelloAdminBeginRoleActionCommand(
                "access-token",
                action,
                fixture.Role.Id,
                targetUserId,
                parameters,
                ClientKey: null),
            CancellationToken.None);

        Assert.True(begun.IsSuccess);

        var completed = await fixture.Application.CompleteRoleActionAsync(
            new HelloAdminCompleteRoleActionCommand(
                "access-token",
                action,
                fixture.Role.Id,
                targetUserId,
                parameters,
                begun.Value.ChallengeId,
                "123456"),
            CancellationToken.None);

        Assert.True(completed.IsSuccess);
        if (action == HelloAdminRoleAction.Assign)
        {
            Assert.NotNull(fixture.Roles.Assigned);
        }
        else
        {
            Assert.NotNull(fixture.Roles.Removed);
        }

        Assert.Equal(targetUserId, fixture.Sessions.RevokedUserId);
    }

    [Fact]
    public async Task AdministratorCannotRemoveOwnProtectedRole()
    {
        var fixture = new Fixture(
            roleName: HelloAdminDefaults.AdministratorRole);

        var result = await fixture.Application.BeginRoleActionAsync(
            new HelloAdminBeginRoleActionCommand(
                "access-token",
                HelloAdminRoleAction.Remove,
                fixture.Role.Id,
                fixture.Actor.Id,
                new HelloAdminRoleActionParameters(),
                ClientKey: null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == HelloAdminErrorCodes.SelfRoleRemovalForbidden);
    }

    [Fact]
    public async Task AdministratorCannotRemoveOwnConfiguredProtectedRole()
    {
        var fixture = new Fixture(
            roleName: "iq-teacher",
            configureOptions: options =>
                options.ProtectedRoleNames = [" IQ-TEACHER "]);

        var result = await fixture.Application.BeginRoleActionAsync(
            new HelloAdminBeginRoleActionCommand(
                "access-token",
                HelloAdminRoleAction.Remove,
                fixture.Role.Id,
                fixture.Actor.Id,
                new HelloAdminRoleActionParameters(),
                ClientKey: null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == HelloAdminErrorCodes.SelfRoleRemovalForbidden);
    }

    [Theory]
    [InlineData(HelloAdminRoleAction.Create)]
    [InlineData(HelloAdminRoleAction.Update)]
    [InlineData(HelloAdminRoleAction.Delete)]
    public async Task DisabledRoleManagementRejectsRoleCrud(
        HelloAdminRoleAction action)
    {
        var fixture = new Fixture(
            configureOptions: options =>
                options.RoleManagementEnabled = false);
        var roleId = action == HelloAdminRoleAction.Create
            ? (Guid?)null
            : fixture.Role.Id;
        var parameters = action switch
        {
            HelloAdminRoleAction.Create =>
                new HelloAdminRoleActionParameters(Name: "new-role"),
            HelloAdminRoleAction.Update =>
                new HelloAdminRoleActionParameters(
                    ExpectedVersion: fixture.Role.Version,
                    Name: "renamed"),
            HelloAdminRoleAction.Delete =>
                new HelloAdminRoleActionParameters(
                    ExpectedVersion: fixture.Role.Version),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        var result = await fixture.Application.BeginRoleActionAsync(
            new HelloAdminBeginRoleActionCommand(
                "access-token",
                action,
                roleId,
                TargetUserId: null,
                parameters,
                ClientKey: null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == HelloAdminErrorCodes.RoleManagementDisabled);
        Assert.Null(fixture.StepUp.BeginCommand);
    }

    [Fact]
    public async Task DisabledRoleManagementRejectsDirectCompletion()
    {
        var fixture = new Fixture(
            configureOptions: options =>
                options.RoleManagementEnabled = false);

        var result = await fixture.Application.CompleteRoleActionAsync(
            new HelloAdminCompleteRoleActionCommand(
                "access-token",
                HelloAdminRoleAction.Update,
                fixture.Role.Id,
                TargetUserId: null,
                new HelloAdminRoleActionParameters(
                    ExpectedVersion: fixture.Role.Version,
                    Name: "renamed"),
                Guid.NewGuid(),
                "123456"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == HelloAdminErrorCodes.RoleManagementDisabled);
        Assert.Null(fixture.StepUp.AuthorizeCommand);
    }

    [Fact]
    public async Task MembershipChangeReportsCommittedStateWhenCleanupFails()
    {
        var fixture = new Fixture(
            revokeResult: OperationResultFactory.Fail(
                new Error(
                    "test.session_cleanup_failed",
                    "cleanup failed",
                    ErrorType.Failure)));
        var targetUserId = Guid.NewGuid();
        var begun = await fixture.Application.BeginRoleActionAsync(
            new HelloAdminBeginRoleActionCommand(
                "access-token",
                HelloAdminRoleAction.Assign,
                fixture.Role.Id,
                targetUserId,
                new HelloAdminRoleActionParameters(),
                ClientKey: null),
            CancellationToken.None);

        var completed = await fixture.Application.CompleteRoleActionAsync(
            new HelloAdminCompleteRoleActionCommand(
                "access-token",
                HelloAdminRoleAction.Assign,
                fixture.Role.Id,
                targetUserId,
                new HelloAdminRoleActionParameters(),
                begun.Value.ChallengeId,
                "123456"),
            CancellationToken.None);

        Assert.False(completed.IsSuccess);
        Assert.NotNull(fixture.Roles.Assigned);
        Assert.Contains(
            completed.Errors,
            error => error.Code
                == HelloAdminErrorCodes.SessionCleanupRequired);
        Assert.DoesNotContain(
            completed.Errors,
            error => error.Code == HelloAdminErrorCodes.RestartRequired);
    }

    [Fact]
    public async Task RoleCrudWritesActorAndResourceToSecurityAudit()
    {
        var fixture = new Fixture();
        var parameters = new HelloAdminRoleActionParameters(
            Name: "Operators",
            Description: "Support access");
        var begun = await fixture.Application.BeginRoleActionAsync(
            new HelloAdminBeginRoleActionCommand(
                "access-token",
                HelloAdminRoleAction.Create,
                RoleId: null,
                TargetUserId: null,
                parameters,
                ClientKey: null),
            CancellationToken.None);

        var completed = await fixture.Application.CompleteRoleActionAsync(
            new HelloAdminCompleteRoleActionCommand(
                "access-token",
                HelloAdminRoleAction.Create,
                RoleId: null,
                TargetUserId: null,
                parameters,
                begun.Value.ChallengeId,
                "123456"),
            CancellationToken.None);

        Assert.True(completed.IsSuccess);
        Assert.NotNull(fixture.SecurityEvents.Event);
        Assert.Equal(
            HelloAdminSecurityEventTypes.RoleCreated,
            fixture.SecurityEvents.Event.EventType);
        Assert.Equal(
            fixture.Actor.Id,
            fixture.SecurityEvents.Event.ActorUserId);
        Assert.Equal(
            fixture.Role.Id,
            fixture.SecurityEvents.Event.ResourceId);
    }

    private sealed class Fixture
    {
        public Fixture(
            string roleName = "Operators",
            OperationResult? revokeResult = null,
            Action<SkopkaHelloAdminOptions>? configureOptions = null)
        {
            Actor = CreateUser(Guid.NewGuid());
            Role = new IdentityRole(
                Guid.NewGuid(),
                roleName,
                "test role",
                ParentId: null,
                Version: 4,
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow);
            RoleQueries = new FakeRoleQueryService(Role);
            Roles = new FakeRoleService(Role);
            Sessions = new FakeSessionService(Actor, revokeResult);
            StepUp = new FakeStepUpService();
            Messages = new FakeMessageSender();
            SecurityEvents = new RecordingSecurityEventSink();
            var options = new SkopkaHelloAdminOptions();
            configureOptions?.Invoke(options);
            Application = new HelloAdminRoleApplication<TestProfile>(
                RoleQueries,
                Roles,
                Sessions,
                StepUp,
                new FakeVerificationService(),
                Messages,
                new HelloDeliveryOptions
                {
                    VerificationChannel = HelloDeliveryChannel.Email,
                },
                options,
                SecurityEvents,
                new HttpContextAccessor());
        }

        public IdentityUser<TestProfile> Actor { get; }

        public IdentityRole Role { get; }

        public FakeRoleQueryService RoleQueries { get; }

        public FakeRoleService Roles { get; }

        public FakeSessionService Sessions { get; }

        public FakeStepUpService StepUp { get; }

        public FakeMessageSender Messages { get; }

        public RecordingSecurityEventSink SecurityEvents { get; }

        public HelloAdminRoleApplication<TestProfile> Application { get; }
    }

    private static IdentityUser<TestProfile> CreateUser(Guid id)
        => new(
            id,
            UserFlags.None,
            "admin",
            "admin@example.test",
            EmailConfirmed: true,
            Phone: null,
            PhoneConfirmed: false,
            new TestProfile("Admin"),
            Version: 2,
            SecurityStamp: "stamp",
            DeletedAt: null,
            BlockedAt: null,
            BlockedUntil: null,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow);

    private sealed record TestProfile(string DisplayName);

    private sealed class FakeRoleQueryService(IdentityRole role)
        : IIdentityRoleQueryService<TestProfile>
    {
        public IdentityRoleQuery? Query { get; private set; }

        public Task<OperationResult<IdentityRolePage>> QueryAsync(
            IdentityRoleQuery query,
            CancellationToken ct)
        {
            Query = query;
            return Task.FromResult(
                OperationResultFactory.Success(
                    new IdentityRolePage([role], null)));
        }
    }

    private sealed class FakeRoleService(IdentityRole role)
        : IIdentityRoleService<TestProfile>
    {
        public AssignRoleCommand? Assigned { get; private set; }

        public RemoveRoleCommand? Removed { get; private set; }

        public Task<IdentityRole?> FindByIdAsync(
            Guid roleId,
            CancellationToken ct)
            => Task.FromResult<IdentityRole?>(
                roleId == role.Id ? role : null);

        public Task<IdentityRole?> FindByNameAsync(
            string roleName,
            CancellationToken ct)
            => Task.FromResult<IdentityRole?>(
                string.Equals(
                    roleName,
                    role.Name,
                    StringComparison.OrdinalIgnoreCase)
                    ? role
                    : null);

        public Task<OperationResult<IdentityRole>> CreateAsync(
            CreateRoleCommand cmd,
            CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success(role));

        public Task<OperationResult<IdentityRole>> UpdateAsync(
            UpdateRoleCommand cmd,
            CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success(role));

        public Task<OperationResult> DeleteAsync(
            DeleteRoleCommand cmd,
            CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success());

        public Task<OperationResult<IReadOnlyList<IdentityRole>>>
            GetUserRolesAsync(Guid userId, CancellationToken ct)
        {
            IReadOnlyList<IdentityRole> result = [];
            return Task.FromResult(OperationResultFactory.Success(result));
        }

        public Task<OperationResult<bool>> IsUserInRoleAsync(
            Guid userId,
            Guid roleId,
            CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success(false));

        public Task<OperationResult> AssignAsync(
            AssignRoleCommand cmd,
            CancellationToken ct)
        {
            Assigned = cmd;
            return Task.FromResult(OperationResultFactory.Success());
        }

        public Task<OperationResult> RemoveAsync(
            RemoveRoleCommand cmd,
            CancellationToken ct)
        {
            Removed = cmd;
            return Task.FromResult(OperationResultFactory.Success());
        }
    }

    private sealed class FakeSessionService(
        IdentityUser<TestProfile> actor,
        OperationResult? revokeResult)
        : IIdentitySessionService<TestProfile>
    {
        public string? ValidatedAccessToken { get; private set; }

        public Guid? RevokedUserId { get; private set; }

        public Task<OperationResult<IdentityUser<TestProfile>>>
            ValidateAccessTokenAsync(
                string accessToken,
                CancellationToken ct)
        {
            ValidatedAccessToken = accessToken;
            return Task.FromResult(OperationResultFactory.Success(actor));
        }

        public Task<OperationResult> RevokeAllAsync(
            RevokeAllIdentitySessionsCommand command,
            CancellationToken ct)
        {
            RevokedUserId = command.UserId;
            return Task.FromResult(
                revokeResult ?? OperationResultFactory.Success());
        }

        public Task<OperationResult<IssuedIdentitySession>> CreateAsync(
            CreateIdentitySessionCommand command,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<OperationResult<IssuedIdentitySession>> RefreshAsync(
            RefreshIdentitySessionCommand command,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<OperationResult> RevokeAsync(
            RevokeIdentitySessionCommand command,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<OperationResult> RevokeByIdAsync(
            RevokeIdentitySessionByIdCommand command,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<OperationResult<IReadOnlyList<IdentitySessionInfo>>>
            ListAsync(
                ListIdentitySessionsCommand command,
                CancellationToken ct) => throw new NotSupportedException();

        public Task<int> PruneAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeStepUpService
        : IIdentityStepUpService<TestProfile>
    {
        private readonly Guid challengeId = Guid.NewGuid();

        public BeginStepUpCommand? BeginCommand { get; private set; }

        public AuthorizeStepUpCommand? AuthorizeCommand { get; private set; }

        public Task<OperationResult<IssuedVerificationChallenge>> BeginAsync(
            BeginStepUpCommand cmd,
            CancellationToken ct)
        {
            BeginCommand = cmd;
            return Task.FromResult(
                OperationResultFactory.Success(
                    new IssuedVerificationChallenge(
                        challengeId,
                        VerificationMethods.OneTimeCode,
                        DateTimeOffset.UtcNow.AddMinutes(5),
                        "123456")));
        }

        public Task<OperationResult<StepUpDecision>> AuthorizeAsync(
            AuthorizeStepUpCommand cmd,
            CancellationToken ct)
        {
            AuthorizeCommand = cmd;
            return Task.FromResult(
                OperationResultFactory.Success(
                    new StepUpDecision(
                        cmd.UserId,
                        cmd.Action,
                        cmd.Binding,
                        "purpose",
                        cmd.ChallengeId,
                        VerificationMethods.OneTimeCode,
                        2,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow)));
        }
    }

    private sealed class FakeVerificationService
        : IIdentityVerificationService<TestProfile>
    {
        public Task<OperationResult<VerificationProof>> VerifyAsync(
            VerifyVerificationChallengeCommand cmd,
            CancellationToken ct)
            => Task.FromResult(
                OperationResultFactory.Success(
                    new VerificationProof(
                        cmd.ChallengeId,
                        "proof",
                        DateTimeOffset.UtcNow.AddMinutes(1))));

        public Task<OperationResult<IssuedVerificationChallenge>> BeginAsync(
            BeginVerificationCommand cmd,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<OperationResult> ConsumeAsync(
            ConsumeVerificationProofCommand cmd,
            CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeMessageSender : IHelloAccountMessageSender
    {
        public HelloAccountMessage? Message { get; private set; }

        public Task<OperationResult> SendAsync(
            HelloAccountMessage message,
            CancellationToken cancellationToken)
        {
            Message = message;
            return Task.FromResult(OperationResultFactory.Success());
        }
    }

    private sealed class RecordingSecurityEventSink
        : IHelloSecurityEventSink
    {
        public HelloSecurityEventEnvelope? Event { get; private set; }

        public OperationResult Write(
            HelloSecurityEventEnvelope securityEvent)
        {
            Event = securityEvent;
            return OperationResultFactory.Success();
        }
    }
}
