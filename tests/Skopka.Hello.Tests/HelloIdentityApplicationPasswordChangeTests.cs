using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Credentials;
using Skopka.Identity.Errors;
using Skopka.Identity.Sessions;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Users;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Tests;

public sealed class HelloIdentityApplicationPasswordChangeTests
{
    [Theory]
    [InlineData(
        IdentityErrorCodes.InvalidPassword,
        ErrorType.Unauthorized)]
    [InlineData(
        IdentityErrorCodes.PasswordRejected,
        ErrorType.Validation)]
    [InlineData(
        IdentityErrorCodes.ConcurrencyConflict,
        ErrorType.Conflict)]
    public async Task CredentialFailureAfterVerifiedOtpRequiresNewChallenge(
        string causeCode,
        ErrorType causeType)
    {
        var fixture = CreateFixture(
            credentialResult: OperationResultFactory.Fail(
                new Error(
                    causeCode,
                    "Credential change failed.",
                    causeType)));

        var result = await fixture.Application
            .CompletePasswordChangeAsync(
                CreateCommand(fixture.ChallengeId),
                CancellationToken.None);

        AssertRestartRequired(result, causeCode);
        Assert.Equal(
            [
                "session.validate",
                "verification.verify",
                "step_up.authorize",
                "credential.change",
            ],
            fixture.Calls);
        Assert.False(fixture.Sessions.RevokeCalled);
    }

    [Fact]
    public async Task AuthorizationFailureAfterVerifiedOtpRequiresNewChallenge()
    {
        const string causeCode =
            IdentityErrorCodes.VerificationProofInvalid;
        var fixture = CreateFixture(
            authorizeResult: OperationResultFactory.Fail<StepUpDecision>(
                new Error(
                    causeCode,
                    "Verification proof is invalid.",
                    ErrorType.Unauthorized)));

        var result = await fixture.Application
            .CompletePasswordChangeAsync(
                CreateCommand(fixture.ChallengeId),
                CancellationToken.None);

        AssertRestartRequired(result, causeCode);
        Assert.Equal(
            [
                "session.validate",
                "verification.verify",
                "step_up.authorize",
            ],
            fixture.Calls);
        Assert.False(fixture.Credentials.ChangeCalled);
    }

    [Theory]
    [InlineData(IdentityErrorCodes.VerificationChallengeInvalid)]
    [InlineData(IdentityErrorCodes.VerificationAttemptsExceeded)]
    [InlineData(IdentityErrorCodes.AuthenticationBlocked)]
    [InlineData(IdentityErrorCodes.ConcurrencyConflict)]
    public async Task TerminalVerificationFailureRequiresNewChallenge(
        string causeCode)
    {
        var fixture = CreateFixture(
            verificationResult:
                OperationResultFactory.Fail<VerificationProof>(
                    new Error(
                        causeCode,
                        "The verification challenge cannot be used.",
                        ErrorType.Validation)));

        var result = await fixture.Application
            .CompletePasswordChangeAsync(
                CreateCommand(fixture.ChallengeId),
                CancellationToken.None);

        AssertRestartRequired(result, causeCode);
        Assert.Equal(
            ["session.validate", "verification.verify"],
            fixture.Calls);
        Assert.False(fixture.StepUp.AuthorizeCalled);
    }

    [Fact]
    public async Task WrongOtpRemainsRetryable()
    {
        var fixture = CreateFixture(
            verificationResult:
                OperationResultFactory.Fail<VerificationProof>(
                    new Error(
                        IdentityErrorCodes.VerificationResponseInvalid,
                        "Verification response is invalid.",
                        ErrorType.Unauthorized)));

        var result = await fixture.Application
            .CompletePasswordChangeAsync(
                CreateCommand(fixture.ChallengeId),
                CancellationToken.None);

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            IdentityErrorCodes.VerificationResponseInvalid,
            error.Code);
        Assert.False(fixture.StepUp.AuthorizeCalled);
    }

    [Fact]
    public async Task SessionRevocationFailureReportsChangedPassword()
    {
        const string causeCode = "test.session.revoke_failed";
        var fixture = CreateFixture(
            revokeResult: OperationResultFactory.Fail(
                new Error(
                    causeCode,
                    "Session revocation failed.",
                    ErrorType.Failure)));

        var result = await fixture.Application
            .CompletePasswordChangeAsync(
                CreateCommand(fixture.ChallengeId),
                CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            HelloPasswordChangeErrorCodes.SessionCleanupRequired,
            result.Errors.First().Code);
        Assert.Contains(
            result.Errors,
            error => error.Code == causeCode);
        Assert.True(fixture.Credentials.ChangeCalled);
        Assert.True(fixture.Sessions.RevokeCalled);
    }

    private static void AssertRestartRequired(
        OperationResult result,
        string causeCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(
            HelloPasswordChangeErrorCodes.RestartRequired,
            result.Errors.First().Code);
        Assert.Contains(
            result.Errors,
            error => string.Equals(
                error.Code,
                causeCode,
                StringComparison.Ordinal));
    }

    private static HelloCompletePasswordChangeCommand CreateCommand(
        Guid challengeId)
        => new(
            "access-token",
            challengeId,
            "123456",
            "current password",
            "new password");

    private static Fixture CreateFixture(
        OperationResult<VerificationProof>? verificationResult = null,
        OperationResult<StepUpDecision>? authorizeResult = null,
        OperationResult? credentialResult = null,
        OperationResult? revokeResult = null)
    {
        var calls = new List<string>();
        var user = new IdentityUser<TestProfile>(
            Guid.NewGuid(),
            UserFlags.None,
            "alice",
            "alice@example.test",
            true,
            "+15551234567",
            true,
            new TestProfile("Alice"),
            7,
            "SECURITY-STAMP",
            null,
            null,
            null,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(-1));
        var challengeId = Guid.NewGuid();
        var verification = new FakeVerificationService(
            verificationResult
            ?? OperationResultFactory.Success(
                new VerificationProof(
                    challengeId,
                    "proof",
                    DateTimeOffset.UtcNow.AddMinutes(1))),
            calls);
        var stepUp = new FakeStepUpService(
            authorizeResult
            ?? OperationResultFactory.Success(
                new StepUpDecision(
                    user.Id,
                    HelloAccountSecurity.PasswordChangeAction,
                    "binding",
                    HelloAccountSecurity.PasswordChangePurpose,
                    challengeId,
                    VerificationMethods.OneTimeCode,
                    2,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow)),
            calls);
        var credentials = new FakeCredentialService(
            credentialResult
            ?? OperationResultFactory.Success(),
            calls);
        var sessions = new FakeSessionService(
            user,
            revokeResult
            ?? OperationResultFactory.Success(),
            calls);
        var application = new HelloIdentityApplication<TestProfile>(
            null!,
            null!,
            sessions,
            credentials,
            null!,
            stepUp,
            verification,
            null!,
            null!,
            new HelloDeliveryOptions(),
            new SkopkaHelloOptions());
        return new Fixture(
            application,
            challengeId,
            calls,
            sessions,
            credentials,
            stepUp);
    }

    private sealed record Fixture(
        HelloIdentityApplication<TestProfile> Application,
        Guid ChallengeId,
        List<string> Calls,
        FakeSessionService Sessions,
        FakeCredentialService Credentials,
        FakeStepUpService StepUp);

    private sealed class FakeVerificationService(
        OperationResult<VerificationProof> result,
        List<string> calls)
        : IIdentityVerificationService<TestProfile>
    {
        public Task<OperationResult<IssuedVerificationChallenge>> BeginAsync(
            BeginVerificationCommand cmd,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<VerificationProof>> VerifyAsync(
            VerifyVerificationChallengeCommand cmd,
            CancellationToken ct)
        {
            calls.Add("verification.verify");
            return Task.FromResult(result);
        }

        public Task<OperationResult> ConsumeAsync(
            ConsumeVerificationProofCommand cmd,
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeStepUpService(
        OperationResult<StepUpDecision> result,
        List<string> calls)
        : IIdentityStepUpService<TestProfile>
    {
        public bool AuthorizeCalled { get; private set; }

        public Task<OperationResult<IssuedVerificationChallenge>> BeginAsync(
            BeginStepUpCommand cmd,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<StepUpDecision>> AuthorizeAsync(
            AuthorizeStepUpCommand cmd,
            CancellationToken ct)
        {
            AuthorizeCalled = true;
            calls.Add("step_up.authorize");
            return Task.FromResult(result);
        }
    }

    private sealed class FakeCredentialService(
        OperationResult changeResult,
        List<string> calls)
        : IPasswordCredentialService<TestProfile>
    {
        public bool ChangeCalled { get; private set; }

        public Task<OperationResult> ChangePasswordAsync(
            ChangePasswordCommand cmd,
            CancellationToken ct)
        {
            ChangeCalled = true;
            calls.Add("credential.change");
            return Task.FromResult(changeResult);
        }

        public Task<OperationResult> SetPasswordAsync(
            SetPasswordCommand cmd,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult> RemovePasswordAsync(
            RemovePasswordCommand cmd,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult> ResetPasswordAsync(
            ResetPasswordCommand cmd,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult> VerifyPasswordAsync(
            VerifyPasswordCommand cmd,
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeSessionService(
        IdentityUser<TestProfile> user,
        OperationResult revokeResult,
        List<string> calls)
        : IIdentitySessionService<TestProfile>
    {
        public bool RevokeCalled { get; private set; }

        public Task<OperationResult<IdentityUser<TestProfile>>>
            ValidateAccessTokenAsync(
                string accessToken,
                CancellationToken ct)
        {
            calls.Add("session.validate");
            return Task.FromResult(
                OperationResultFactory.Success(user));
        }

        public Task<OperationResult> RevokeAllAsync(
            RevokeAllIdentitySessionsCommand command,
            CancellationToken ct)
        {
            RevokeCalled = true;
            calls.Add("session.revoke_all");
            return Task.FromResult(revokeResult);
        }

        public Task<OperationResult<IssuedIdentitySession>> CreateAsync(
            CreateIdentitySessionCommand command,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<IssuedIdentitySession>> RefreshAsync(
            RefreshIdentitySessionCommand command,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult> RevokeAsync(
            RevokeIdentitySessionCommand command,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult> RevokeByIdAsync(
            RevokeIdentitySessionByIdCommand command,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<IReadOnlyList<IdentitySessionInfo>>>
            ListAsync(
                ListIdentitySessionsCommand command,
                CancellationToken ct)
            => throw new NotSupportedException();

        public Task<int> PruneAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed record TestProfile(string DisplayName);
}
