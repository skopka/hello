using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
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
    [InlineData(
        true,
        HelloAccountMessageKind.ExternalLoginLinkVerification,
        HelloDeliveryChannel.Email)]
    [InlineData(
        false,
        HelloAccountMessageKind.ExternalLoginUnlinkVerification,
        HelloDeliveryChannel.Email)]
    [InlineData(
        true,
        HelloAccountMessageKind.ExternalLoginLinkVerification,
        HelloDeliveryChannel.Sms)]
    [InlineData(
        false,
        HelloAccountMessageKind.ExternalLoginUnlinkVerification,
        HelloDeliveryChannel.Sms)]
    public async Task BeginMutationUsesPurposeAndConfiguredChannel(
        bool link,
        HelloAccountMessageKind expectedKind,
        HelloDeliveryChannel expectedChannel)
    {
        var calls = new List<string>();
        var user = CreateUser(version: 7, securityStamp: "STAMP");
        var now = DateTimeOffset.UtcNow;
        var challenge = new IssuedVerificationChallenge(
            Guid.NewGuid(),
            VerificationMethods.OneTimeCode,
            now.AddMinutes(2),
            "123456");
        var sessions = new FakeSessionService(
            user,
            new IssuedIdentitySession(
                Guid.NewGuid(),
                "unused-access",
                now.AddMinutes(5),
                "unused-refresh",
                now.AddDays(1)),
            calls);
        var stepUp = new FakeStepUpService(calls, challenge);
        var sender = new RecordingMessageSender();
        var application = new HelloExternalIdentityApplication<TestProfile>(
            null!,
            new UnexpectedRegistrationService(),
            sessions,
            new UnexpectedSignInMethodQueryService(),
            stepUp,
            null!,
            sender,
            new HelloDeliveryOptions
            {
                VerificationChannel = expectedChannel,
            },
            new SkopkaHelloOptions());
        var command = new HelloBeginExternalLoginMutationCommand(
            "old-access-token",
            new ExternalLoginKey("github", "subject-1"),
            "client-key");

        var result = link
            ? await application.BeginLinkAsync(
                command,
                CancellationToken.None)
            : await application.BeginUnlinkAsync(
                command,
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        var message = Assert.Single(sender.Messages);
        Assert.NotEqual(Guid.Empty, message.MessageId);
        Assert.Equal(expectedKind, message.Kind);
        Assert.Equal(expectedChannel, message.Channel);
        Assert.Equal(
            expectedChannel == HelloDeliveryChannel.Email
                ? user.Email
                : user.Phone,
            message.RecipientAddress);
        Assert.Equal("123456", message.VerificationCode);
        Assert.Null(message.ActionUrl);
    }

    [Fact]
    public async Task BeginMutationDoesNotIssueChallengeWithoutDeliveryRoute()
    {
        var calls = new List<string>();
        var now = DateTimeOffset.UtcNow;
        var user = CreateUser(version: 7, securityStamp: "STAMP");
        var sessions = new FakeSessionService(
            user,
            new IssuedIdentitySession(
                Guid.NewGuid(),
                "unused-access",
                now.AddMinutes(5),
                "unused-refresh",
                now.AddDays(1)),
            calls);
        var application = new HelloExternalIdentityApplication<TestProfile>(
            null!,
            new UnexpectedRegistrationService(),
            sessions,
            new UnexpectedSignInMethodQueryService(),
            new FakeStepUpService(
                calls,
                new IssuedVerificationChallenge(
                    Guid.NewGuid(),
                    VerificationMethods.OneTimeCode,
                    now.AddMinutes(2),
                    "123456")),
            null!,
            new HelloAccountMessageDispatcher(
                new HelloDeliveryOptions(),
                []),
            new HelloDeliveryOptions(),
            new SkopkaHelloOptions());

        var result = await application.BeginLinkAsync(
            new HelloBeginExternalLoginMutationCommand(
                "old-access-token",
                new ExternalLoginKey("github", "subject-1"),
                "client-key"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            HelloDeliveryErrorCodes.NotConfigured,
            Assert.Single(result.Errors).Code);
        Assert.Equal(["session.validate"], calls);
    }

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
            new HelloAccountMessageDispatcher(
                new HelloDeliveryOptions(),
                []),
            new HelloDeliveryOptions(),
            new SkopkaHelloOptions());
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
            HelloAccountSecurity.CreateExternalLoginBinding(
                login,
                user.Id,
                HelloDeliveryChannel.Email,
                user.Email!),
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

    [Fact]
    public async Task UnrelatedProfileChangeKeepsIssuedChallengeValid()
    {
        var calls = new List<string>();
        var before = CreateUser(version: 7, securityStamp: "STAMP");
        var after = before with
        {
            Profile = new TestProfile("Alice Updated"),
            Version = 8,
            ModifiedAt = DateTimeOffset.UtcNow,
        };
        var mutated = after with
        {
            Version = 9,
            SecurityStamp = "NEW-STAMP",
        };
        var login = new ExternalLoginKey("github", "subject-1");
        var challenge = new IssuedVerificationChallenge(
            Guid.NewGuid(),
            VerificationMethods.OneTimeCode,
            DateTimeOffset.UtcNow.AddMinutes(2),
            "123456");
        var proof = new VerificationProof(
            challenge.ChallengeId,
            "one-time-proof",
            DateTimeOffset.UtcNow.AddMinutes(1));
        var issuedSession = new IssuedIdentitySession(
            Guid.NewGuid(),
            "fresh-access-token",
            DateTimeOffset.UtcNow.AddMinutes(5),
            "fresh-refresh-token",
            DateTimeOffset.UtcNow.AddDays(1));
        var sessions = new FakeSessionService(
            before,
            issuedSession,
            calls);
        var stepUp = new FakeStepUpService(calls, challenge);
        var externalLogins = new FakeExternalLoginService(
            mutated,
            calls);
        var application = new HelloExternalIdentityApplication<TestProfile>(
            externalLogins,
            new UnexpectedRegistrationService(),
            sessions,
            new UnexpectedSignInMethodQueryService(),
            stepUp,
            new FakeVerificationService(proof, calls),
            new RecordingMessageSender(),
            new HelloDeliveryOptions(),
            new SkopkaHelloOptions());

        var begun = await application.BeginLinkAsync(
            new HelloBeginExternalLoginMutationCommand(
                "old-access-token",
                login,
                "client-key"),
            CancellationToken.None);
        Assert.True(begun.IsSuccess);

        sessions.ValidatedUser = after;
        var completed = await application.CompleteLinkAsync(
            new HelloCompleteExternalLoginMutationCommand(
                "old-access-token",
                login,
                after.Version,
                challenge.ChallengeId,
                "123456",
                new IdentitySessionMetadata("Browser", "Device")),
            CancellationToken.None);

        Assert.True(completed.IsSuccess);
        Assert.Equal(
            stepUp.LastBeginCommand?.Binding,
            stepUp.LastCommand?.Binding);
        Assert.Equal(
            after.Version,
            externalLogins.LastLinkCommand?.ExpectedVersion);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DestinationOrTargetChangeInvalidatesIssuedChallenge(
        bool changeDestination,
        bool changeTarget)
    {
        var calls = new List<string>();
        var before = CreateUser(version: 7, securityStamp: "STAMP");
        var after = changeDestination
            ? before with
            {
                Email = "other@example.test",
                Version = 8,
                ModifiedAt = DateTimeOffset.UtcNow,
            }
            : before;
        var initialLogin = new ExternalLoginKey(
            "github",
            "subject-1");
        var completedLogin = changeTarget
            ? new ExternalLoginKey("github", "subject-2")
            : initialLogin;
        var challenge = new IssuedVerificationChallenge(
            Guid.NewGuid(),
            VerificationMethods.OneTimeCode,
            DateTimeOffset.UtcNow.AddMinutes(2),
            "123456");
        var proof = new VerificationProof(
            challenge.ChallengeId,
            "one-time-proof",
            DateTimeOffset.UtcNow.AddMinutes(1));
        var sessions = new FakeSessionService(
            before,
            new IssuedIdentitySession(
                Guid.NewGuid(),
                "unused-access",
                DateTimeOffset.UtcNow.AddMinutes(5),
                "unused-refresh",
                DateTimeOffset.UtcNow.AddDays(1)),
            calls);
        var stepUp = new FakeStepUpService(calls, challenge);
        var externalLogins = new FakeExternalLoginService(after, calls);
        var application = new HelloExternalIdentityApplication<TestProfile>(
            externalLogins,
            new UnexpectedRegistrationService(),
            sessions,
            new UnexpectedSignInMethodQueryService(),
            stepUp,
            new FakeVerificationService(proof, calls),
            new RecordingMessageSender(),
            new HelloDeliveryOptions(),
            new SkopkaHelloOptions());

        var begun = await application.BeginLinkAsync(
            new HelloBeginExternalLoginMutationCommand(
                "old-access-token",
                initialLogin,
                "client-key"),
            CancellationToken.None);
        Assert.True(begun.IsSuccess);

        sessions.ValidatedUser = after;
        var completed = await application.CompleteLinkAsync(
            new HelloCompleteExternalLoginMutationCommand(
                "old-access-token",
                completedLogin,
                after.Version,
                challenge.ChallengeId,
                "123456",
                new IdentitySessionMetadata("Browser", "Device")),
            CancellationToken.None);

        Assert.False(completed.IsSuccess);
        Assert.Equal(
            HelloExternalIdentityErrorCodes.ChallengeRestartRequired,
            completed.Errors.First().Code);
        Assert.Contains(
            completed.Errors,
            error => error.Code
                == "test.step_up.binding_mismatch");
        Assert.Null(externalLogins.LastLinkCommand);
    }

    [Fact]
    public async Task VersionRaceBeforeVerificationDoesNotConsumeOtp()
    {
        var calls = new List<string>();
        var current = CreateUser(
            version: 8,
            securityStamp: "STAMP");
        var sessions = new FakeSessionService(
            current,
            new IssuedIdentitySession(
                Guid.NewGuid(),
                "unused-access",
                DateTimeOffset.UtcNow.AddMinutes(5),
                "unused-refresh",
                DateTimeOffset.UtcNow.AddDays(1)),
            calls);
        var externalLogins = new FakeExternalLoginService(current, calls);
        var application = new HelloExternalIdentityApplication<TestProfile>(
            externalLogins,
            new UnexpectedRegistrationService(),
            sessions,
            new UnexpectedSignInMethodQueryService(),
            new FakeStepUpService(calls),
            new FakeVerificationService(
                new VerificationProof(
                    Guid.NewGuid(),
                    "unused-proof",
                    DateTimeOffset.UtcNow.AddMinutes(1)),
                calls),
            new RecordingMessageSender(),
            new HelloDeliveryOptions(),
            new SkopkaHelloOptions());

        var completed = await application.CompleteLinkAsync(
            new HelloCompleteExternalLoginMutationCommand(
                "old-access-token",
                new ExternalLoginKey("github", "subject-1"),
                7,
                Guid.NewGuid(),
                "123456",
                new IdentitySessionMetadata("Browser", "Device")),
            CancellationToken.None);

        Assert.False(completed.IsSuccess);
        Assert.Equal(
            IdentityErrorCodes.ConcurrencyConflict,
            Assert.Single(completed.Errors).Code);
        Assert.Equal(["session.validate"], calls);
        Assert.Null(externalLogins.LastLinkCommand);
    }

    [Fact]
    public async Task VersionRaceDuringMutationIsTerminal()
    {
        var calls = new List<string>();
        var current = CreateUser(
            version: 7,
            securityStamp: "STAMP");
        var challengeId = Guid.NewGuid();
        var sessions = new FakeSessionService(
            current,
            new IssuedIdentitySession(
                Guid.NewGuid(),
                "unused-access",
                DateTimeOffset.UtcNow.AddMinutes(5),
                "unused-refresh",
                DateTimeOffset.UtcNow.AddDays(1)),
            calls);
        var externalLogins = new FakeExternalLoginService(current, calls)
        {
            LinkResult = OperationResultFactory.Fail<
                IdentityUser<TestProfile>>(
                    new Error(
                        IdentityErrorCodes.ConcurrencyConflict,
                        "Concurrency conflict.",
                        ErrorType.Conflict)),
        };
        var application = new HelloExternalIdentityApplication<TestProfile>(
            externalLogins,
            new UnexpectedRegistrationService(),
            sessions,
            new UnexpectedSignInMethodQueryService(),
            new FakeStepUpService(calls),
            new FakeVerificationService(
                new VerificationProof(
                    challengeId,
                    "one-time-proof",
                    DateTimeOffset.UtcNow.AddMinutes(1)),
                calls),
            new RecordingMessageSender(),
            new HelloDeliveryOptions(),
            new SkopkaHelloOptions());

        var completed = await application.CompleteLinkAsync(
            new HelloCompleteExternalLoginMutationCommand(
                "old-access-token",
                new ExternalLoginKey("github", "subject-1"),
                current.Version,
                challengeId,
                "123456",
                new IdentitySessionMetadata("Browser", "Device")),
            CancellationToken.None);

        Assert.False(completed.IsSuccess);
        Assert.Equal(
            HelloExternalIdentityErrorCodes.RestartRequired,
            completed.Errors.First().Code);
        Assert.Contains(
            completed.Errors,
            error => error.Code
                == IdentityErrorCodes.ConcurrencyConflict);
        Assert.NotNull(externalLogins.LastLinkCommand);
        Assert.Null(sessions.LastRevokeAllCommand);
        Assert.Null(sessions.LastCreateCommand);
        Assert.Equal(
            [
                "session.validate",
                "verification.verify",
                "step_up.authorize",
                "external.link",
            ],
            calls);
    }

    [Theory]
    [InlineData(
        IdentityErrorCodes.VerificationChallengeInvalid,
        true)]
    [InlineData(
        IdentityErrorCodes.VerificationAttemptsExceeded,
        true)]
    [InlineData(
        IdentityErrorCodes.AuthenticationBlocked,
        true)]
    [InlineData(
        IdentityErrorCodes.ConcurrencyConflict,
        true)]
    [InlineData(
        IdentityErrorCodes.VerificationResponseInvalid,
        false)]
    public async Task VerificationFailureIsTerminalUnlessResponseIsWrong(
        string causeCode,
        bool restartRequired)
    {
        var calls = new List<string>();
        var current = CreateUser(
            version: 7,
            securityStamp: "STAMP");
        var challengeId = Guid.NewGuid();
        var sessions = new FakeSessionService(
            current,
            new IssuedIdentitySession(
                Guid.NewGuid(),
                "unused-access",
                DateTimeOffset.UtcNow.AddMinutes(5),
                "unused-refresh",
                DateTimeOffset.UtcNow.AddDays(1)),
            calls);
        var verification = new FakeVerificationService(
            new VerificationProof(
                challengeId,
                "unused-proof",
                DateTimeOffset.UtcNow.AddMinutes(1)),
            calls)
        {
            VerifyResult = OperationResultFactory.Fail<VerificationProof>(
                new Error(
                    causeCode,
                    "Verification failed.",
                    ErrorType.Unauthorized)),
        };
        var externalLogins = new FakeExternalLoginService(current, calls);
        var application = new HelloExternalIdentityApplication<TestProfile>(
            externalLogins,
            new UnexpectedRegistrationService(),
            sessions,
            new UnexpectedSignInMethodQueryService(),
            new FakeStepUpService(calls),
            verification,
            new RecordingMessageSender(),
            new HelloDeliveryOptions(),
            new SkopkaHelloOptions());

        var completed = await application.CompleteLinkAsync(
            new HelloCompleteExternalLoginMutationCommand(
                "old-access-token",
                new ExternalLoginKey("github", "subject-1"),
                current.Version,
                challengeId,
                "123456",
                new IdentitySessionMetadata("Browser", "Device")),
            CancellationToken.None);

        Assert.False(completed.IsSuccess);
        Assert.Equal(
            restartRequired
                ? HelloExternalIdentityErrorCodes
                    .ChallengeRestartRequired
                : causeCode,
            completed.Errors.First().Code);
        if (restartRequired)
        {
            Assert.Contains(
                completed.Errors,
                error => error.Code == causeCode);
        }
        else
        {
            Assert.Single(completed.Errors);
        }

        Assert.Equal(
            ["session.validate", "verification.verify"],
            calls);
        Assert.Null(externalLogins.LastLinkCommand);
    }

    [Fact]
    public async Task AuthorizationFailureAfterVerifiedOtpIsTerminal()
    {
        const string causeCode =
            IdentityErrorCodes.VerificationProofInvalid;
        var calls = new List<string>();
        var current = CreateUser(
            version: 7,
            securityStamp: "STAMP");
        var challengeId = Guid.NewGuid();
        var sessions = new FakeSessionService(
            current,
            new IssuedIdentitySession(
                Guid.NewGuid(),
                "unused-access",
                DateTimeOffset.UtcNow.AddMinutes(5),
                "unused-refresh",
                DateTimeOffset.UtcNow.AddDays(1)),
            calls);
        var stepUp = new FakeStepUpService(calls)
        {
            AuthorizeResult = OperationResultFactory.Fail<StepUpDecision>(
                new Error(
                    causeCode,
                    "Verification proof is invalid.",
                    ErrorType.Unauthorized)),
        };
        var externalLogins = new FakeExternalLoginService(current, calls);
        var application = new HelloExternalIdentityApplication<TestProfile>(
            externalLogins,
            new UnexpectedRegistrationService(),
            sessions,
            new UnexpectedSignInMethodQueryService(),
            stepUp,
            new FakeVerificationService(
                new VerificationProof(
                    challengeId,
                    "proof",
                    DateTimeOffset.UtcNow.AddMinutes(1)),
                calls),
            new RecordingMessageSender(),
            new HelloDeliveryOptions(),
            new SkopkaHelloOptions());

        var completed = await application.CompleteLinkAsync(
            new HelloCompleteExternalLoginMutationCommand(
                "old-access-token",
                new ExternalLoginKey("github", "subject-1"),
                current.Version,
                challengeId,
                "123456",
                new IdentitySessionMetadata("Browser", "Device")),
            CancellationToken.None);

        Assert.False(completed.IsSuccess);
        Assert.Equal(
            HelloExternalIdentityErrorCodes.ChallengeRestartRequired,
            completed.Errors.First().Code);
        Assert.Contains(
            completed.Errors,
            error => error.Code == causeCode);
        Assert.Null(externalLogins.LastLinkCommand);
        Assert.Equal(
            [
                "session.validate",
                "verification.verify",
                "step_up.authorize",
            ],
            calls);
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("session")]
    public async Task FinalizationFailureAfterMutationIsTerminal(
        string failureStage)
    {
        var causeCode = $"test.{failureStage}.failed";
        var calls = new List<string>();
        var current = CreateUser(
            version: 7,
            securityStamp: "OLD-STAMP");
        var mutated = current with
        {
            Version = 8,
            SecurityStamp = "NEW-STAMP",
        };
        var challengeId = Guid.NewGuid();
        var sessions = new FakeSessionService(
            current,
            new IssuedIdentitySession(
                Guid.NewGuid(),
                "fresh-access",
                DateTimeOffset.UtcNow.AddMinutes(5),
                "fresh-refresh",
                DateTimeOffset.UtcNow.AddDays(1)),
            calls)
        {
            RevokeAllResult = failureStage == "revoke"
                ? OperationResultFactory.Fail(
                    new Error(
                        causeCode,
                        "Session revocation failed.",
                        ErrorType.Failure))
                : null,
            CreateResult = failureStage == "session"
                ? OperationResultFactory.Fail<IssuedIdentitySession>(
                    new Error(
                        causeCode,
                        "Session creation failed.",
                        ErrorType.Failure))
                : null,
        };
        var externalLogins = new FakeExternalLoginService(mutated, calls);
        var application = new HelloExternalIdentityApplication<TestProfile>(
            externalLogins,
            new UnexpectedRegistrationService(),
            sessions,
            new UnexpectedSignInMethodQueryService(),
            new FakeStepUpService(calls),
            new FakeVerificationService(
                new VerificationProof(
                    challengeId,
                    "proof",
                    DateTimeOffset.UtcNow.AddMinutes(1)),
                calls),
            new RecordingMessageSender(),
            new HelloDeliveryOptions(),
            new SkopkaHelloOptions());

        var completed = await application.CompleteLinkAsync(
            new HelloCompleteExternalLoginMutationCommand(
                "old-access-token",
                new ExternalLoginKey("github", "subject-1"),
                current.Version,
                challengeId,
                "123456",
                new IdentitySessionMetadata("Browser", "Device")),
            CancellationToken.None);

        Assert.False(completed.IsSuccess);
        Assert.Equal(
            HelloExternalIdentityErrorCodes.RestartRequired,
            completed.Errors.First().Code);
        Assert.Contains(
            completed.Errors,
            error => error.Code == causeCode);
        string[] expectedCalls = failureStage == "revoke"
            ?
            [
                "session.validate",
                "verification.verify",
                "step_up.authorize",
                "external.link",
                "session.revoke_all",
            ]
            :
            [
                "session.validate",
                "verification.verify",
                "step_up.authorize",
                "external.link",
                "session.revoke_all",
                "session.create",
            ];
        Assert.Equal(
            expectedCalls,
            calls);
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
            "+15551234567",
            true,
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
        public OperationResult<IdentityUser<TestProfile>>? LinkResult
        {
            get;
            init;
        }

        public OperationResult<IdentityUser<TestProfile>>? UnlinkResult
        {
            get;
            init;
        }

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
                LinkResult
                ?? OperationResultFactory.Success(mutatedUser));
        }

        public Task<OperationResult<IdentityUser<TestProfile>>> UnlinkAsync(
            UnlinkExternalLoginCommand command,
            CancellationToken ct)
        {
            calls.Add("external.unlink");
            LastUnlinkCommand = command;
            return Task.FromResult(
                UnlinkResult
                ?? OperationResultFactory.Success(mutatedUser));
        }
    }

    private sealed class FakeSessionService(
        IdentityUser<TestProfile> validatedUser,
        IssuedIdentitySession issuedSession,
        List<string> calls)
        : IIdentitySessionService<TestProfile>
    {
        public OperationResult<IssuedIdentitySession>? CreateResult
        {
            get;
            init;
        }

        public OperationResult? RevokeAllResult
        {
            get;
            init;
        }

        public IdentityUser<TestProfile> ValidatedUser
        {
            get;
            set;
        } = validatedUser;

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
                CreateResult
                ?? OperationResultFactory.Success(issuedSession));
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
                OperationResultFactory.Success(ValidatedUser));
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
            return Task.FromResult(
                RevokeAllResult
                ?? OperationResultFactory.Success());
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
        public OperationResult<VerificationProof>? VerifyResult
        {
            get;
            init;
        }

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
                VerifyResult
                ?? OperationResultFactory.Success(proof));
        }

        public Task<OperationResult> ConsumeAsync(
            ConsumeVerificationProofCommand cmd,
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeStepUpService(
        List<string> calls,
        IssuedVerificationChallenge? beginResult = null)
        : IIdentityStepUpService<TestProfile>
    {
        public OperationResult<StepUpDecision>? AuthorizeResult
        {
            get;
            init;
        }

        public BeginStepUpCommand? LastBeginCommand
        {
            get;
            private set;
        }

        public AuthorizeStepUpCommand? LastCommand { get; private set; }

        public Task<OperationResult<IssuedVerificationChallenge>> BeginAsync(
            BeginStepUpCommand cmd,
            CancellationToken ct)
        {
            if (beginResult is null)
            {
                throw new NotSupportedException();
            }

            calls.Add("step_up.begin");
            LastBeginCommand = cmd;
            return Task.FromResult(
                OperationResultFactory.Success(beginResult));
        }

        public Task<OperationResult<StepUpDecision>> AuthorizeAsync(
            AuthorizeStepUpCommand cmd,
            CancellationToken ct)
        {
            calls.Add("step_up.authorize");
            LastCommand = cmd;
            if (AuthorizeResult is not null)
            {
                return Task.FromResult(AuthorizeResult);
            }

            if (LastBeginCommand is not null
                && (LastBeginCommand.UserId != cmd.UserId
                    || !string.Equals(
                        LastBeginCommand.Action,
                        cmd.Action,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        LastBeginCommand.Binding,
                        cmd.Binding,
                        StringComparison.Ordinal)))
            {
                return Task.FromResult(
                    OperationResultFactory.Fail<StepUpDecision>(
                        new Error(
                            "test.step_up.binding_mismatch",
                            "The step-up binding changed.",
                            ErrorType.Unauthorized)));
            }

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

    private sealed class RecordingMessageSender
        : IHelloAccountMessageSender
    {
        public List<HelloAccountMessage> Messages { get; } = [];

        public Task<OperationResult> SendAsync(
            HelloAccountMessage message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            return Task.FromResult(OperationResultFactory.Success());
        }
    }

    private sealed record TestProfile(string DisplayName);
}
