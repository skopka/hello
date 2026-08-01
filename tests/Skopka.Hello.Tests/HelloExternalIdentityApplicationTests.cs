using Skopka.Abstraction.OperationResult;
using Skopka.Identity.ExternalLogins;
using Skopka.Identity.Registration;
using Skopka.Identity.Sessions;
using Skopka.Identity.SignInMethods;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Users;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Tests;

public sealed class HelloExternalIdentityApplicationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletedMutationRevokesOldSessionsAndIssuesFreshSession(
        bool link)
    {
        var calls = new List<string>();
        var user = CreateUser(version: 7, securityStamp: "OLD-STAMP");
        var mutated = user with
        {
            Version = 8,
            SecurityStamp = "NEW-STAMP",
            ModifiedAt = DateTimeOffset.UtcNow,
        };
        var login = new ExternalLoginKey("github", "subject-1");
        var challengeId = Guid.NewGuid();
        var proof = new VerificationProof(
            challengeId,
            "one-time-proof",
            DateTimeOffset.UtcNow.AddMinutes(1));
        var issued = new IssuedIdentitySession(
            Guid.NewGuid(),
            "fresh-access-token",
            DateTimeOffset.UtcNow.AddMinutes(5),
            "fresh-refresh-token",
            DateTimeOffset.UtcNow.AddDays(1));
        var externalLogins = new FakeExternalLoginService(
            mutated,
            calls);
        var sessions = new FakeSessionService(user, issued, calls);
        var verification = new FakeVerificationService(proof, calls);
        var stepUp = new FakeStepUpService(calls);
        var application = new HelloExternalIdentityApplication<TestProfile>(
            externalLogins,
            new UnexpectedRegistrationService(),
            sessions,
            new UnexpectedSignInMethodQueryService(),
            stepUp,
            verification,
            new NullHelloAccountMessageSender());
        var metadata = new IdentitySessionMetadata(
            "Browser",
            "Trusted device");
        var command = new HelloCompleteExternalLoginMutationCommand(
            "old-access-token",
            login,
            user.Version,
            challengeId,
            "123456",
            metadata);

        var result = link
            ? await application.CompleteLinkAsync(
                command,
                CancellationToken.None)
            : await application.CompleteUnlinkAsync(
                command,
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(mutated.Version, result.Value.Account.Version);
        Assert.Equal(
            issued.AccessToken,
            result.Value.Session.AccessToken);
        Assert.Equal(
            issued.RefreshToken,
            result.Value.Session.RefreshToken);
        Assert.Equal(
            [
                "session.validate",
                "verification.verify",
                "step_up.authorize",
                link ? "external.link" : "external.unlink",
                "session.revoke_all",
                "session.create",
            ],
            calls);

        Assert.Equal(user.Id, verification.LastCommand?.UserId);
        Assert.Equal(challengeId, verification.LastCommand?.ChallengeId);
        Assert.Equal("123456", verification.LastCommand?.Response);
        Assert.Equal(user.Id, stepUp.LastCommand?.UserId);
        Assert.Equal(
            link
                ? HelloAccountSecurity.ExternalLinkAction
                : HelloAccountSecurity.ExternalUnlinkAction,
            stepUp.LastCommand?.Action);
        Assert.Equal(
            HelloAccountSecurity.CreateExternalLoginBinding(login),
            stepUp.LastCommand?.Binding);
        Assert.Equal(challengeId, stepUp.LastCommand?.ChallengeId);
        Assert.Equal(proof.Token, stepUp.LastCommand?.Proof);

        if (link)
        {
            Assert.Equal(
                new LinkExternalLoginCommand(
                    user.Id,
                    user.Version,
                    login),
                externalLogins.LastLinkCommand);
            Assert.Null(externalLogins.LastUnlinkCommand);
        }
        else
        {
            Assert.Equal(
                new UnlinkExternalLoginCommand(
                    user.Id,
                    user.Version,
                    login),
                externalLogins.LastUnlinkCommand);
            Assert.Null(externalLogins.LastLinkCommand);
        }

        Assert.Equal(
            new RevokeAllIdentitySessionsCommand(user.Id),
            sessions.LastRevokeAllCommand);
        Assert.Equal(user.Id, sessions.LastCreateCommand?.UserId);
        Assert.Equal(
            mutated.SecurityStamp,
            sessions.LastCreateCommand?.SecurityStamp);
        Assert.Equal(metadata, sessions.LastCreateCommand?.Metadata);
    }

    private static IdentityUser<TestProfile> CreateUser(
        long version,
        string securityStamp)
        => new(
            Guid.NewGuid(),
            UserFlags.None,
            "alice",
            "alice@example.test",
            true,
            null,
            false,
            new TestProfile("Alice"),
            version,
            securityStamp,
            null,
            null,
            null,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(-1));

    private sealed class FakeExternalLoginService(
        IdentityUser<TestProfile> mutatedUser,
        List<string> calls)
        : IExternalLoginService<TestProfile>
    {
        public LinkExternalLoginCommand? LastLinkCommand
        {
            get;
            private set;
        }

        public UnlinkExternalLoginCommand? LastUnlinkCommand
        {
            get;
            private set;
        }

        public Task<OperationResult<IdentityUser<TestProfile>>> ResolveAsync(
            ExternalLoginKey login,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<IReadOnlyList<ExternalLoginInfo>>>
            ListAsync(Guid userId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<IdentityUser<TestProfile>>> LinkAsync(
            LinkExternalLoginCommand command,
            CancellationToken ct)
        {
            calls.Add("external.link");
            LastLinkCommand = command;
            return Task.FromResult(
                OperationResultFactory.Success(mutatedUser));
        }

        public Task<OperationResult<IdentityUser<TestProfile>>> UnlinkAsync(
            UnlinkExternalLoginCommand command,
            CancellationToken ct)
        {
            calls.Add("external.unlink");
            LastUnlinkCommand = command;
            return Task.FromResult(
                OperationResultFactory.Success(mutatedUser));
        }
    }

    private sealed class FakeSessionService(
        IdentityUser<TestProfile> validatedUser,
        IssuedIdentitySession issuedSession,
        List<string> calls)
        : IIdentitySessionService<TestProfile>
    {
        public CreateIdentitySessionCommand? LastCreateCommand
        {
            get;
            private set;
        }

        public RevokeAllIdentitySessionsCommand? LastRevokeAllCommand
        {
            get;
            private set;
        }

        public Task<OperationResult<IssuedIdentitySession>> CreateAsync(
            CreateIdentitySessionCommand command,
            CancellationToken ct)
        {
            calls.Add("session.create");
            LastCreateCommand = command;
            return Task.FromResult(
                OperationResultFactory.Success(issuedSession));
        }

        public Task<OperationResult<IssuedIdentitySession>> RefreshAsync(
            RefreshIdentitySessionCommand command,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<IdentityUser<TestProfile>>>
            ValidateAccessTokenAsync(
                string accessToken,
                CancellationToken ct)
        {
            calls.Add("session.validate");
            Assert.Equal("old-access-token", accessToken);
            return Task.FromResult(
                OperationResultFactory.Success(validatedUser));
        }

        public Task<OperationResult> RevokeAsync(
            RevokeIdentitySessionCommand command,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult> RevokeByIdAsync(
            RevokeIdentitySessionByIdCommand command,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult> RevokeAllAsync(
            RevokeAllIdentitySessionsCommand command,
            CancellationToken ct)
        {
            calls.Add("session.revoke_all");
            LastRevokeAllCommand = command;
            return Task.FromResult(OperationResultFactory.Success());
        }

        public Task<OperationResult<IReadOnlyList<IdentitySessionInfo>>>
            ListAsync(
                ListIdentitySessionsCommand command,
                CancellationToken ct)
            => throw new NotSupportedException();

        public Task<int> PruneAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeVerificationService(
        VerificationProof proof,
        List<string> calls)
        : IIdentityVerificationService<TestProfile>
    {
        public VerifyVerificationChallengeCommand? LastCommand
        {
            get;
            private set;
        }

        public Task<OperationResult<IssuedVerificationChallenge>> BeginAsync(
            BeginVerificationCommand cmd,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<VerificationProof>> VerifyAsync(
            VerifyVerificationChallengeCommand cmd,
            CancellationToken ct)
        {
            calls.Add("verification.verify");
            LastCommand = cmd;
            return Task.FromResult(
                OperationResultFactory.Success(proof));
        }

        public Task<OperationResult> ConsumeAsync(
            ConsumeVerificationProofCommand cmd,
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeStepUpService(List<string> calls)
        : IIdentityStepUpService<TestProfile>
    {
        public AuthorizeStepUpCommand? LastCommand { get; private set; }

        public Task<OperationResult<IssuedVerificationChallenge>> BeginAsync(
            BeginStepUpCommand cmd,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<StepUpDecision>> AuthorizeAsync(
            AuthorizeStepUpCommand cmd,
            CancellationToken ct)
        {
            calls.Add("step_up.authorize");
            LastCommand = cmd;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(
                OperationResultFactory.Success(
                    new StepUpDecision(
                        cmd.UserId,
                        cmd.Action,
                        cmd.Binding,
                        "test-purpose",
                        cmd.ChallengeId,
                        VerificationMethods.OneTimeCode,
                        2,
                        now,
                        now)));
        }
    }

    private sealed class UnexpectedRegistrationService
        : IIdentityRegistrationService<TestProfile>
    {
        public Task<OperationResult<IdentityUser<TestProfile>>>
            RegisterPasswordAsync(
                RegisterPasswordUserCommand<TestProfile> command,
                CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<IdentityUser<TestProfile>>>
            RegisterExternalAsync(
                RegisterExternalUserCommand<TestProfile> command,
                CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class UnexpectedSignInMethodQueryService
        : IIdentitySignInMethodQueryService<TestProfile>
    {
        public Task<OperationResult<SignInMethodSnapshot>> GetAsync(
            Guid userId,
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed record TestProfile(string DisplayName);
}
