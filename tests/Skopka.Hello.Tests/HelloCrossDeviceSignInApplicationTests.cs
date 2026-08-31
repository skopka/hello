using Skopka.Abstraction.OperationResult;
using Skopka.Identity.DeviceAuthorization;
using Skopka.Identity.Errors;
using Skopka.Identity.Sessions;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Users;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Tests;

public sealed class HelloCrossDeviceSignInApplicationTests
{
    private const string DeviceCode = "device-code";

    [Fact]
    public async Task UnsafeReturnUrlIsRejectedBeforePersistence()
    {
        var fixture = CreateFixture();

        var result = await fixture.Application.BeginAsync(
            new HelloBeginCrossDeviceSignInCommand(
                "https://attacker.example/continue",
                null,
                "127.0.0.1",
                "browser",
                "device",
                new IdentitySessionMetadata("client", "device")),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(fixture.DeviceAuthorization.CreateCalled);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == "hello.cross_device.return_url_invalid");
    }

    [Fact]
    public async Task ApprovalUsesFreshTotpBoundToRequestAndUser()
    {
        var fixture = CreateFixture();

        var challenge = await fixture.Application.BeginApprovalAsync(
            new HelloBeginCrossDeviceApprovalCommand(
                "actor-token",
                DeviceCode,
                "client-key"),
            CancellationToken.None);
        var approved = await fixture.Application.ApproveAsync(
            new HelloApproveCrossDeviceSignInCommand(
                "actor-token",
                DeviceCode,
                challenge.Value.ChallengeId,
                "123456",
                "client-key"),
            CancellationToken.None);

        Assert.True(challenge.IsSuccess);
        Assert.True(approved.IsSuccess);
        Assert.Equal(
            VerificationMethods.TimeBasedOneTimePassword,
            fixture.StepUp.BeginCommand?.Method);
        Assert.Equal(DeviceCode, fixture.StepUp.BeginCommand?.Binding);
        Assert.Equal("123456", fixture.Verification.VerifyCommand?.Response);
        var command = Assert.IsType<
            ApproveDeviceAuthorizationRequestCommand>(
                fixture.DeviceAuthorization.ApproveCommand);
        Assert.Equal(fixture.User.Id, command.UserId);
        Assert.Equal(DeviceCode, command.StepUpDecision.Binding);
        Assert.Equal(
            VerificationMethods.TimeBasedOneTimePassword,
            command.StepUpDecision.Method);
    }

    [Fact]
    public async Task IncorrectTotpDoesNotApproveRequest()
    {
        var fixture = CreateFixture(verificationSucceeds: false);

        var result = await fixture.Application.ApproveAsync(
            new HelloApproveCrossDeviceSignInCommand(
                "actor-token",
                DeviceCode,
                fixture.ChallengeId,
                "000000",
                "client-key"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(fixture.StepUp.AuthorizeCommand);
        Assert.Null(fixture.DeviceAuthorization.ApproveCommand);
    }

    [Fact]
    public async Task ApprovalDetailsRequireAnOnlineValidatedSession()
    {
        var fixture = CreateFixture(sessionValid: false);

        var result = await fixture.Application.GetApprovalDetailsAsync(
            "expired-token",
            DeviceCode,
            "client-key",
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(fixture.DeviceAuthorization.DetailsCalled);
    }

    [Fact]
    public async Task ApprovalRequestCanBeResolvedByUserCode()
    {
        var fixture = CreateFixture();

        var result = await fixture.Application
            .GetApprovalDetailsByUserCodeAsync(
                "actor-token",
                "abcd efgh",
                "client-key",
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeviceCode, result.Value.DeviceCode);
        Assert.True(fixture.DeviceAuthorization.UserCodeDetailsCalled);
    }

    [Fact]
    public async Task CompletionReturnsTheIndependentConsumedSession()
    {
        var fixture = CreateFixture();

        var result = await fixture.Application.CompleteAsync(
            DeviceCode,
            "browser-verifier",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.Issued.SessionId, result.Value.SignIn.Session.SessionId);
        Assert.Equal("/connect/authorize?client_id=native", result.Value.ReturnUrl);
        Assert.Equal("native", result.Value.ClientId);
        Assert.Equal(
            "browser-verifier",
            fixture.DeviceAuthorization.ConsumeCommand?.BrowserVerifier);
        Assert.Equal(
            fixture.Issued.AccessToken,
            fixture.Sessions.ValidatedAccessToken);
    }

    [Fact]
    public async Task DenialUsesTheAuthenticatedUser()
    {
        var fixture = CreateFixture();

        var result = await fixture.Application.DenyAsync(
            new HelloDenyCrossDeviceSignInCommand(
                "actor-token",
                DeviceCode),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.User.Id, fixture.DeviceAuthorization.DenyCommand?.UserId);
    }

    private static Fixture CreateFixture(
        bool verificationSucceeds = true,
        bool sessionValid = true)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new IdentityUser<TestProfile>(
            Guid.NewGuid(),
            UserFlags.None,
            "alice",
            "alice@example.test",
            true,
            null,
            false,
            new TestProfile("Alice"),
            1,
            "STAMP",
            null,
            null,
            null,
            now,
            now);
        var challengeId = Guid.NewGuid();
        var issued = new IssuedIdentitySession(
            Guid.NewGuid(),
            "device-access-token",
            now.AddMinutes(15),
            "device-refresh-token",
            now.AddDays(14));
        var deviceAuthorization = new FakeDeviceAuthorizationService(
            issued,
            now);
        var sessions = new FakeSessionService(
            user,
            sessionValid);
        var verification = new FakeVerificationService(
            challengeId,
            verificationSucceeds,
            now);
        var stepUp = new FakeStepUpService(
            user.Id,
            challengeId,
            now);
        var application = new HelloCrossDeviceSignInApplication<TestProfile>(
            deviceAuthorization,
            stepUp,
            verification,
            sessions,
            []);
        return new Fixture(
            application,
            deviceAuthorization,
            sessions,
            verification,
            stepUp,
            user,
            issued,
            challengeId);
    }

    private sealed record Fixture(
        HelloCrossDeviceSignInApplication<TestProfile> Application,
        FakeDeviceAuthorizationService DeviceAuthorization,
        FakeSessionService Sessions,
        FakeVerificationService Verification,
        FakeStepUpService StepUp,
        IdentityUser<TestProfile> User,
        IssuedIdentitySession Issued,
        Guid ChallengeId);

    private sealed class FakeDeviceAuthorizationService(
        IssuedIdentitySession issued,
        DateTimeOffset now)
        : IIdentityDeviceAuthorizationService<TestProfile>
    {
        public bool CreateCalled { get; private set; }

        public bool DetailsCalled { get; private set; }

        public bool UserCodeDetailsCalled { get; private set; }

        public ApproveDeviceAuthorizationRequestCommand? ApproveCommand
        { get; private set; }

        public DenyDeviceAuthorizationRequestCommand? DenyCommand
        { get; private set; }

        public ConsumeDeviceAuthorizationRequestCommand? ConsumeCommand
        { get; private set; }

        public Task<OperationResult<CreatedDeviceAuthorizationRequest>>
            CreateAsync(
                CreateDeviceAuthorizationRequestCommand command,
                CancellationToken ct)
        {
            CreateCalled = true;
            return Task.FromResult(
                OperationResultFactory.Success(
                    new CreatedDeviceAuthorizationRequest(
                        Guid.NewGuid(),
                        DeviceCode,
                        "browser-verifier",
                        "ABCD-EFGH",
                        now,
                        now.AddMinutes(2))));
        }

        public Task<OperationResult<DeviceAuthorizationStatus>> GetStatusAsync(
            GetDeviceAuthorizationStatusCommand command,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<DeviceAuthorizationApprovalDetails>>
            GetApprovalDetailsAsync(
                GetDeviceAuthorizationApprovalDetailsCommand command,
                CancellationToken ct)
        {
            DetailsCalled = true;
            return Task.FromResult(
                OperationResultFactory.Success(
                    new DeviceAuthorizationApprovalDetails(
                        Guid.NewGuid(),
                        command.DeviceCode,
                        "ABCD-EFGH",
                        DeviceAuthorizationState.Pending,
                        now,
                        now.AddMinutes(2),
                        "127.0.0.1",
                        "browser",
                        "device")));
        }

        public Task<OperationResult<DeviceAuthorizationApprovalDetails>>
            GetApprovalDetailsByUserCodeAsync(
                GetDeviceAuthorizationApprovalDetailsByUserCodeCommand command,
                CancellationToken ct)
        {
            UserCodeDetailsCalled = true;
            return Task.FromResult(
                OperationResultFactory.Success(
                    new DeviceAuthorizationApprovalDetails(
                        Guid.NewGuid(),
                        DeviceCode,
                        "ABCD-EFGH",
                        DeviceAuthorizationState.Pending,
                        now,
                        now.AddMinutes(2),
                        "127.0.0.1",
                        "browser",
                        "device")));
        }

        public Task<OperationResult> ApproveAsync(
            ApproveDeviceAuthorizationRequestCommand command,
            CancellationToken ct)
        {
            ApproveCommand = command;
            return Task.FromResult(OperationResultFactory.Success());
        }

        public Task<OperationResult> DenyAsync(
            DenyDeviceAuthorizationRequestCommand command,
            CancellationToken ct)
        {
            DenyCommand = command;
            return Task.FromResult(OperationResultFactory.Success());
        }

        public Task<OperationResult<ConsumedDeviceAuthorizationRequest>>
            ConsumeAsync(
                ConsumeDeviceAuthorizationRequestCommand command,
                CancellationToken ct)
        {
            ConsumeCommand = command;
            return Task.FromResult(
                OperationResultFactory.Success(
                    new ConsumedDeviceAuthorizationRequest(
                        issued,
                        "native",
                        "/connect/authorize?client_id=native")));
        }

        public Task<int> PruneAsync(CancellationToken ct)
            => Task.FromResult(0);
    }

    private sealed class FakeVerificationService(
        Guid challengeId,
        bool succeeds,
        DateTimeOffset now)
        : IIdentityVerificationService<TestProfile>
    {
        public VerifyVerificationChallengeCommand? VerifyCommand
        { get; private set; }

        public Task<OperationResult<IssuedVerificationChallenge>> BeginAsync(
            BeginVerificationCommand command,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<VerificationProof>> VerifyAsync(
            VerifyVerificationChallengeCommand command,
            CancellationToken ct)
        {
            VerifyCommand = command;
            return Task.FromResult(
                succeeds
                    ? OperationResultFactory.Success(
                        new VerificationProof(
                            challengeId,
                            "proof",
                            now.AddMinutes(1),
                            VerificationMethods.TimeBasedOneTimePassword))
                    : OperationResultFactory.Fail<VerificationProof>(
                        new Error(
                            IdentityErrorCodes.TotpCodeInvalid,
                            "The authenticator code is invalid.",
                            ErrorType.Validation)));
        }

        public Task<OperationResult> ConsumeAsync(
            ConsumeVerificationProofCommand command,
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeStepUpService(
        Guid userId,
        Guid challengeId,
        DateTimeOffset now)
        : IIdentityStepUpService<TestProfile>
    {
        public BeginStepUpCommand? BeginCommand { get; private set; }

        public AuthorizeStepUpCommand? AuthorizeCommand { get; private set; }

        public Task<OperationResult<IssuedVerificationChallenge>> BeginAsync(
            BeginStepUpCommand command,
            CancellationToken ct)
        {
            BeginCommand = command;
            return Task.FromResult(
                OperationResultFactory.Success(
                    new IssuedVerificationChallenge(
                        challengeId,
                        VerificationMethods.TimeBasedOneTimePassword,
                        now.AddMinutes(1),
                        null)));
        }

        public Task<OperationResult<StepUpDecision>> AuthorizeAsync(
            AuthorizeStepUpCommand command,
            CancellationToken ct)
        {
            AuthorizeCommand = command;
            return Task.FromResult(
                OperationResultFactory.Success(
                    new StepUpDecision(
                        userId,
                        command.Action,
                        command.Binding,
                        "hello:device_authorization.approve",
                        challengeId,
                        VerificationMethods.TimeBasedOneTimePassword,
                        2,
                        now,
                        now)));
        }
    }

    private sealed class FakeSessionService(
        IdentityUser<TestProfile> user,
        bool sessionValid)
        : IIdentitySessionService<TestProfile>
    {
        public string? ValidatedAccessToken { get; private set; }

        public Task<OperationResult<IdentityUser<TestProfile>>>
            ValidateAccessTokenAsync(
                string accessToken,
                CancellationToken ct)
        {
            ValidatedAccessToken = accessToken;
            return Task.FromResult(
                sessionValid
                    ? OperationResultFactory.Success(user)
                    : OperationResultFactory.Fail<IdentityUser<TestProfile>>(
                        new Error(
                            IdentityErrorCodes.AccessTokenInvalid,
                            "The access token is invalid.",
                            ErrorType.Unauthorized)));
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

        public Task<OperationResult> RevokeAllAsync(
            RevokeAllIdentitySessionsCommand command,
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
