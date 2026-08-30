using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Admin;
using Skopka.Identity;
using Skopka.Identity.Sessions;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Tokens;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Commands;
using Skopka.Identity.Users.Queries;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Tests;

public sealed class HelloAdminApplicationTests
{
    [Fact]
    public async Task QueryProjectsProfileForCurrentAdministrator()
    {
        var actor = CreateUser(Guid.NewGuid(), "Admin", version: 3);
        var target = CreateUser(Guid.NewGuid(), "Target", version: 7);
        var projector = new FakeProfileProjector();
        var application = new HelloAdminApplication<TestProfile>(
            new FakeUserQueryService(target),
            null!,
            new FakeSessionService(actor),
            null!,
            null!,
            null!,
            projector,
            null!,
            new HelloDeliveryOptions(),
            new SkopkaHelloAdminOptions());

        var result = await application.QueryUsersAsync(
            new HelloAdminQueryUsersCommand("access-token"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(target.Id, item.Id);
        Assert.Equal("Target", Assert.Single(item.Profile).Value);
        Assert.Equal(actor.Id, projector.Context?.ActorUserId);
        Assert.Equal(target.Id, projector.Context?.TargetUserId);
    }

    [Theory]
    [InlineData(HelloAdminUserAction.Block)]
    [InlineData(HelloAdminUserAction.Delete)]
    public async Task BeginRejectsDangerousSelfMutationBeforeStepUp(
        HelloAdminUserAction action)
    {
        var actor = CreateUser(Guid.NewGuid(), "Admin", version: 3);
        var stepUp = new FakeStepUpService();
        var application = new HelloAdminApplication<TestProfile>(
            null!,
            null!,
            new FakeSessionService(actor),
            stepUp,
            null!,
            null!,
            null!,
            null!,
            new HelloDeliveryOptions(),
            new SkopkaHelloAdminOptions());

        var result = await application.BeginUserActionAsync(
            new HelloAdminBeginUserActionCommand(
                "access-token",
                actor.Id,
                action,
                new HelloAdminUserActionParameters(ExpectedVersion: 3),
                ClientKey: "client"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == HelloAdminErrorCodes.SelfMutationForbidden);
        Assert.Null(stepUp.BeginCommand);
    }

    [Fact]
    public async Task BlockUsesSameBoundStepUpAndRevokesTargetSessions()
    {
        var actor = CreateUser(Guid.NewGuid(), "Admin", version: 3);
        var target = CreateUser(Guid.NewGuid(), "Target", version: 7);
        var blocked = target with
        {
            Version = 8,
            BlockedAt = DateTimeOffset.UtcNow,
        };
        var challengeId = Guid.NewGuid();
        var stepUp = new FakeStepUpService(challengeId);
        var verification = new FakeVerificationService(challengeId);
        var sessions = new FakeSessionService(actor);
        var users = new FakeUserService(blocked);
        var sender = new FakeMessageSender();
        var application = new HelloAdminApplication<TestProfile>(
            null!,
            users,
            sessions,
            stepUp,
            verification,
            null!,
            new FakeProfileProjector(),
            sender,
            new HelloDeliveryOptions
            {
                VerificationChannel = HelloDeliveryChannel.Email,
            },
            new SkopkaHelloAdminOptions());
        var parameters = new HelloAdminUserActionParameters(
            ExpectedVersion: 7,
            BlockedUntil: DateTimeOffset.UtcNow.AddHours(1),
            Reason: "incident");

        var began = await application.BeginUserActionAsync(
            new HelloAdminBeginUserActionCommand(
                "access-token",
                target.Id,
                HelloAdminUserAction.Block,
                parameters,
                ClientKey: "client"),
            CancellationToken.None);
        var completed = await application.CompleteUserActionAsync(
            new HelloAdminCompleteUserActionCommand(
                "access-token",
                target.Id,
                HelloAdminUserAction.Block,
                parameters,
                challengeId,
                "123456"),
            CancellationToken.None);

        Assert.True(began.IsSuccess);
        Assert.True(completed.IsSuccess);
        Assert.Equal(
            stepUp.BeginCommand?.Binding,
            stepUp.AuthorizeCommand?.Binding);
        Assert.Equal(
            HelloAdminSecurity.BlockAction,
            stepUp.AuthorizeCommand?.Action);
        Assert.Equal("admin@example.test", sender.Message?.RecipientAddress);
        Assert.Equal(
            HelloAccountMessageKind.AdminActionVerification,
            sender.Message?.Kind);
        Assert.Equal(target.Id, users.BlockCommand?.UserId);
        Assert.Equal(target.Id, sessions.RevokedUserId);
        Assert.Equal(8, completed.Value.User?.Version);
        Assert.True(completed.Value.SessionsRevoked);
    }

    [Fact]
    public async Task SessionCleanupFailureDoesNotInviteMutationReplay()
    {
        var actor = CreateUser(Guid.NewGuid(), "Admin", version: 3);
        var target = CreateUser(Guid.NewGuid(), "Target", version: 7);
        var blocked = target with
        {
            Version = 8,
            BlockedAt = DateTimeOffset.UtcNow,
        };
        var challengeId = Guid.NewGuid();
        var sessions = new FakeSessionService(
            actor,
            OperationResultFactory.Fail(
                new Error(
                    "test.session.cleanup_failed",
                    "Cleanup failed.",
                    ErrorType.Failure)));
        var application = new HelloAdminApplication<TestProfile>(
            null!,
            new FakeUserService(blocked),
            sessions,
            new FakeStepUpService(challengeId),
            new FakeVerificationService(challengeId),
            null!,
            new FakeProfileProjector(),
            new FakeMessageSender(),
            new HelloDeliveryOptions(),
            new SkopkaHelloAdminOptions());

        var result = await application.CompleteUserActionAsync(
            new HelloAdminCompleteUserActionCommand(
                "access-token",
                target.Id,
                HelloAdminUserAction.Block,
                new HelloAdminUserActionParameters(ExpectedVersion: 7),
                challengeId,
                "123456"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == HelloAdminErrorCodes.SessionCleanupRequired);
        Assert.DoesNotContain(
            result.Errors,
            error => error.Code == HelloAdminErrorCodes.RestartRequired);
    }

    [Fact]
    public async Task ManualEmailConfirmationIsDisabledByDefault()
    {
        var actor = CreateUser(Guid.NewGuid(), "Admin", version: 3);
        var target = CreateUser(Guid.NewGuid(), "Target", version: 7) with
        {
            Email = "target@example.test",
        };
        var stepUp = new FakeStepUpService();
        var application = new HelloAdminApplication<TestProfile>(
            new FakeUserQueryService(target),
            null!,
            new FakeSessionService(actor),
            stepUp,
            null!,
            null!,
            null!,
            null!,
            new HelloDeliveryOptions(),
            new SkopkaHelloAdminOptions());

        var result = await application.BeginUserActionAsync(
            new HelloAdminBeginUserActionCommand(
                "access-token",
                target.Id,
                HelloAdminUserAction.ConfirmEmail,
                new HelloAdminUserActionParameters(ExpectedVersion: 7),
                ClientKey: "client"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == HelloAdminErrorCodes.ManualEmailConfirmationDisabled);
        Assert.Null(stepUp.BeginCommand);
    }

    [Fact]
    public async Task ManualEmailConfirmationUsesBoundStepUpAndCurrentEmail()
    {
        var actor = CreateUser(Guid.NewGuid(), "Admin", version: 3);
        var target = CreateUser(Guid.NewGuid(), "Target", version: 7) with
        {
            Email = "target@example.test",
        };
        var confirmed = target with
        {
            EmailConfirmed = true,
            Version = 8,
        };
        var challengeId = Guid.NewGuid();
        var stepUp = new FakeStepUpService(challengeId);
        var users = new FakeUserService(confirmed);
        var actionTokens = new FakeActionTokenIssuer();
        var application = new HelloAdminApplication<TestProfile>(
            new FakeUserQueryService(target),
            users,
            new FakeSessionService(actor),
            stepUp,
            new FakeVerificationService(challengeId),
            actionTokens,
            new FakeProfileProjector(),
            new FakeMessageSender(),
            new HelloDeliveryOptions(),
            new SkopkaHelloAdminOptions
            {
                ManualEmailConfirmationEnabled = true,
            });
        var parameters = new HelloAdminUserActionParameters(
            ExpectedVersion: target.Version);

        var began = await application.BeginUserActionAsync(
            new HelloAdminBeginUserActionCommand(
                "access-token",
                target.Id,
                HelloAdminUserAction.ConfirmEmail,
                parameters,
                ClientKey: "client"),
            CancellationToken.None);
        var completed = await application.CompleteUserActionAsync(
            new HelloAdminCompleteUserActionCommand(
                "access-token",
                target.Id,
                HelloAdminUserAction.ConfirmEmail,
                parameters,
                challengeId,
                "123456"),
            CancellationToken.None);

        Assert.True(began.IsSuccess);
        Assert.True(completed.IsSuccess);
        Assert.Equal(
            HelloAdminSecurity.ConfirmEmailAction,
            stepUp.AuthorizeCommand?.Action);
        Assert.Equal(target.Id, actionTokens.UserId);
        Assert.Equal(target.Id, users.ConfirmEmailCommand?.UserId);
        Assert.Equal(target.Email, users.ConfirmEmailCommand?.Email);
        Assert.Equal("email-token", users.ConfirmEmailCommand?.Token);
        Assert.True(completed.Value.User?.EmailConfirmed);
        Assert.False(completed.Value.SessionsRevoked);
    }

    private static IdentityUser<TestProfile> CreateUser(
        Guid id,
        string displayName,
        long version)
        => new(
            id,
            UserFlags.None,
            displayName.ToLowerInvariant(),
            displayName == "Admin" ? "admin@example.test" : null,
            EmailConfirmed: displayName == "Admin",
            Phone: null,
            PhoneConfirmed: false,
            new TestProfile(displayName),
            version,
            "stamp",
            DeletedAt: null,
            BlockedAt: null,
            BlockedUntil: null,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow);

    private sealed record TestProfile(string DisplayName);

    private sealed class FakeProfileProjector
        : IHelloAdminProfileProjector<TestProfile>
    {
        public HelloAdminProfileProjectionContext? Context { get; private set; }

        public Task<OperationResult<IReadOnlyList<HelloAdminProfileField>>>
            ProjectAsync(
                TestProfile profile,
                HelloAdminProfileProjectionContext context,
                CancellationToken cancellationToken)
        {
            Context = context;
            IReadOnlyList<HelloAdminProfileField> fields =
                [new("displayName", "Display name", profile.DisplayName)];
            return Task.FromResult(OperationResultFactory.Success(fields));
        }
    }

    private sealed class FakeUserQueryService(
        IdentityUser<TestProfile> user)
        : IIdentityUserQueryService<TestProfile>
    {
        public Task<OperationResult<IdentityUserPage<TestProfile>>> QueryAsync(
            IdentityUserQuery query,
            CancellationToken ct)
            => Task.FromResult(
                OperationResultFactory.Success(
                    new IdentityUserPage<TestProfile>([user], null)));
    }

    private sealed class FakeUserService(
        IdentityUser<TestProfile> blocked)
        : IIdentityUserService<TestProfile>
    {
        public BlockUserCommand? BlockCommand { get; private set; }

        public ConfirmEmailCommand? ConfirmEmailCommand { get; private set; }

        public Task<OperationResult<IdentityUser<TestProfile>>> BlockAsync(
            BlockUserCommand cmd,
            CancellationToken ct)
        {
            BlockCommand = cmd;
            return Task.FromResult(OperationResultFactory.Success(blocked));
        }

        public Task<OperationResult<IdentityUser<TestProfile>>> CreateAsync(
            CreateUserCommand<TestProfile> cmd,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<OperationResult<IdentityUser<TestProfile>>> ConfirmEmailAsync(
            ConfirmEmailCommand cmd,
            CancellationToken ct)
        {
            ConfirmEmailCommand = cmd;
            return Task.FromResult(OperationResultFactory.Success(blocked));
        }
        public Task<OperationResult<IdentityUser<TestProfile>>> ConfirmPhoneAsync(
            ConfirmPhoneCommand cmd,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<OperationResult<IdentityUser<TestProfile>>> ChangeUserNameAsync(
            ChangeUserNameCommand cmd,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<OperationResult<IdentityUser<TestProfile>>> ChangeEmailAsync(
            ChangeEmailCommand cmd,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<OperationResult<IdentityUser<TestProfile>>> ChangePhoneAsync(
            ChangePhoneCommand cmd,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<OperationResult<IdentityUser<TestProfile>>> PatchProfileAsync<TPatch>(
            PatchProfileCommand<TPatch> cmd,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<OperationResult<IdentityUser<TestProfile>>> UnblockAsync(
            UnblockUserCommand cmd,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<OperationResult> DeleteAsync(
            DeleteUserCommand cmd,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<OperationResult<IdentityUser<TestProfile>>> RestoreAsync(
            RestoreUserCommand cmd,
            CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeActionTokenIssuer
        : IIdentityActionTokenIssuer<TestProfile>
    {
        public Guid? UserId { get; private set; }

        public Task<OperationResult<IssuedIdentityActionToken>>
            IssueEmailConfirmationAsync(Guid userId, CancellationToken ct)
        {
            UserId = userId;
            return Task.FromResult(
                OperationResultFactory.Success(
                    new IssuedIdentityActionToken(
                        "email-token",
                        DateTimeOffset.UtcNow.AddMinutes(5))));
        }

        public Task<OperationResult<IssuedIdentityActionToken>>
            IssuePhoneConfirmationAsync(Guid userId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<IssuedIdentityActionToken>>
            IssuePasswordResetAsync(Guid userId, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeSessionService(
        IdentityUser<TestProfile> actor,
        OperationResult? revokeResult = null)
        : IIdentitySessionService<TestProfile>
    {
        public Guid? RevokedUserId { get; private set; }

        public Task<OperationResult<IdentityUser<TestProfile>>>
            ValidateAccessTokenAsync(
                string accessToken,
                CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success(actor));

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
        public Task<OperationResult<IReadOnlyList<IdentitySessionInfo>>> ListAsync(
            ListIdentitySessionsCommand command,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<int> PruneAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeStepUpService(Guid? challengeId = null)
        : IIdentityStepUpService<TestProfile>
    {
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
                        challengeId ?? Guid.NewGuid(),
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

    private sealed class FakeVerificationService(Guid challengeId)
        : IIdentityVerificationService<TestProfile>
    {
        public Task<OperationResult<VerificationProof>> VerifyAsync(
            VerifyVerificationChallengeCommand cmd,
            CancellationToken ct)
            => Task.FromResult(
                OperationResultFactory.Success(
                    new VerificationProof(
                        challengeId,
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
}
