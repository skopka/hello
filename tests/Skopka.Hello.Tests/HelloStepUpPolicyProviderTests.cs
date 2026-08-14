using Skopka.Abstraction.OperationResult;
using Skopka.Identity.StepUp;
using Skopka.Identity.ExternalLogins;
using Skopka.Identity.Totp;
using Skopka.Identity.Users;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Tests;

public sealed class HelloStepUpPolicyProviderTests
{
    [Theory]
    [InlineData(
        HelloAccountSecurity.PasswordChangeAction,
        HelloAccountSecurity.PasswordChangePurpose)]
    [InlineData(
        HelloAccountSecurity.PasswordSetAction,
        HelloAccountSecurity.PasswordSetPurpose)]
    [InlineData(
        HelloAccountSecurity.PasswordRemoveAction,
        HelloAccountSecurity.PasswordRemovePurpose)]
    [InlineData(
        HelloAccountSecurity.AccountDeleteAction,
        HelloAccountSecurity.AccountDeletePurpose)]
    [InlineData(
        HelloAccountSecurity.AuthenticatorDisableAction,
        HelloAccountSecurity.AuthenticatorDisablePurpose)]
    [InlineData(
        HelloAccountSecurity.ExternalLinkAction,
        HelloAccountSecurity.ExternalLinkPurpose)]
    [InlineData(
        HelloAccountSecurity.ExternalUnlinkAction,
        HelloAccountSecurity.ExternalUnlinkPurpose)]
    public async Task ProtectedAccountActionsRequireOneTimeCode(
        string action,
        string purpose)
    {
        var provider =
            new HelloStepUpPolicyProvider<object>();

        var requirement = await provider.GetRequirementAsync(
            new StepUpAuthorizationContext(
                Guid.NewGuid(),
                action,
                Guid.NewGuid().ToString("D")),
            CancellationToken.None);

        Assert.NotNull(requirement);
        Assert.Equal(purpose, requirement.Purpose);
        Assert.Equal(2, requirement.AssuranceLevel);
        Assert.Equal(
            TimeSpan.FromMinutes(2),
            requirement.MaximumAge);
        Assert.Equal(
            [VerificationMethods.OneTimeCode],
            requirement.AllowedMethods);
    }

    [Fact]
    public void ExternalLoginBindingIncludesUserTargetAndDeliverySnapshot()
    {
        var first = new ExternalLoginKey("AB", "C");
        var same = new ExternalLoginKey("AB", "C");
        var ambiguousWithoutLengths = new ExternalLoginKey("A", "BC");
        var differentSubjectCase = new ExternalLoginKey("AB", "c");
        var userId = Guid.NewGuid();

        var binding = HelloAccountSecurity
            .CreateExternalLoginBinding(
                first,
                userId,
                HelloDeliveryChannel.Email,
                "alice@example.test");

        Assert.Equal(
            binding,
            HelloAccountSecurity.CreateExternalLoginBinding(
                same,
                userId,
                HelloDeliveryChannel.Email,
                "alice@example.test"));
        Assert.Equal(64, binding.Length);
        Assert.NotEqual(
            binding,
            HelloAccountSecurity.CreateExternalLoginBinding(
                ambiguousWithoutLengths,
                userId,
                HelloDeliveryChannel.Email,
                "alice@example.test"));
        Assert.NotEqual(
            binding,
            HelloAccountSecurity.CreateExternalLoginBinding(
                differentSubjectCase,
                userId,
                HelloDeliveryChannel.Email,
                "alice@example.test"));
        Assert.NotEqual(
            binding,
            HelloAccountSecurity.CreateExternalLoginBinding(
                same,
                Guid.NewGuid(),
                HelloDeliveryChannel.Email,
                "alice@example.test"));
        Assert.NotEqual(
            binding,
            HelloAccountSecurity.CreateExternalLoginBinding(
                same,
                userId,
                HelloDeliveryChannel.Sms,
                "+15551234567"));
        Assert.NotEqual(
            binding,
            HelloAccountSecurity.CreateExternalLoginBinding(
                same,
                userId,
                HelloDeliveryChannel.Email,
                "other@example.test"));
        Assert.DoesNotContain(
            "alice@example.test",
            binding,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PasswordChangeBindingIncludesUserAndDeliverySnapshot()
    {
        var userId = Guid.NewGuid();
        var binding = HelloAccountSecurity.CreateBinding(
            userId,
            HelloDeliveryChannel.Email,
            "alice@example.test");

        Assert.Equal(64, binding.Length);
        Assert.Equal(
            binding,
            HelloAccountSecurity.CreateBinding(
                userId,
                HelloDeliveryChannel.Email,
                "alice@example.test"));
        Assert.NotEqual(
            binding,
            HelloAccountSecurity.CreateBinding(
                Guid.NewGuid(),
                HelloDeliveryChannel.Email,
                "alice@example.test"));
        Assert.NotEqual(
            binding,
            HelloAccountSecurity.CreateBinding(
                userId,
                HelloDeliveryChannel.Sms,
                "+15551234567"));
        Assert.NotEqual(
            binding,
            HelloAccountSecurity.CreateBinding(
                userId,
                HelloDeliveryChannel.Email,
                "other@example.test"));
    }

    [Fact]
    public void SecurityActionBindingSeparatesActions()
    {
        var userId = Guid.NewGuid();
        const string destination = "alice@example.test";

        var passwordSet = HelloAccountSecurity.CreateBinding(
            userId,
            HelloDeliveryChannel.Email,
            destination,
            HelloAccountSecurity.PasswordSetAction);

        Assert.NotEqual(
            passwordSet,
            HelloAccountSecurity.CreateBinding(
                userId,
                HelloDeliveryChannel.Email,
                destination,
                HelloAccountSecurity.PasswordRemoveAction));
        Assert.NotEqual(
            passwordSet,
            HelloAccountSecurity.CreateBinding(
                userId,
                HelloDeliveryChannel.Email,
                destination,
                HelloAccountSecurity.AccountDeleteAction));
    }

    [Fact]
    public async Task UnknownActionHasNoRequirement()
    {
        var provider =
            new HelloStepUpPolicyProvider<object>();

        var requirement = await provider.GetRequirementAsync(
            new StepUpAuthorizationContext(
                Guid.NewGuid(),
                "account.unknown",
                Guid.NewGuid().ToString("D")),
            CancellationToken.None);

        Assert.Null(requirement);
    }

    [Fact]
    public async Task EnabledAuthenticatorReplacesConfirmedContact()
    {
        var user = CreateUserWithoutConfirmedContact();
        var resolver = new HelloStepUpMethodResolver<TestProfile>(
            new HelloDeliveryOptions
            {
                VerificationChannel = HelloDeliveryChannel.Email,
                RequireTotpWhenEnabled = true,
            },
            new UnavailableMessageSender(),
            new FakeTotpService(user.Id, enabled: true));

        var selected = await resolver.SelectAsync(
            user,
            CancellationToken.None);

        Assert.True(selected.IsSuccess);
        Assert.Equal(
            VerificationMethods.TimeBasedOneTimePassword,
            selected.Value.Method);
        Assert.Equal(
            HelloDeliveryChannel.Authenticator,
            selected.Value.Channel);
        Assert.Null(selected.Value.Destination);
    }

    [Fact]
    public async Task PolicyRequiresAuthenticatorOnlyForUsersWhoEnabledIt()
    {
        var userId = Guid.NewGuid();
        var enabledResolver = new HelloStepUpMethodResolver<TestProfile>(
            new HelloDeliveryOptions
            {
                RequireTotpWhenEnabled = true,
            },
            new UnavailableMessageSender(),
            new FakeTotpService(userId, enabled: true));
        var enabledPolicy = new HelloStepUpPolicyProvider<TestProfile>(
            [new HelloAccountStepUpRequirementProvider<TestProfile>()],
            enabledResolver);

        var enabled = await enabledPolicy.GetRequirementAsync(
            new StepUpAuthorizationContext(
                userId,
                HelloAccountSecurity.AccountDeleteAction,
                "binding"),
            CancellationToken.None);

        Assert.NotNull(enabled);
        Assert.Equal(
            [VerificationMethods.TimeBasedOneTimePassword],
            enabled.AllowedMethods);

        var disabledResolver = new HelloStepUpMethodResolver<TestProfile>(
            new HelloDeliveryOptions
            {
                RequireTotpWhenEnabled = true,
            },
            new UnavailableMessageSender(),
            new FakeTotpService(userId, enabled: false));
        var disabledPolicy = new HelloStepUpPolicyProvider<TestProfile>(
            [new HelloAccountStepUpRequirementProvider<TestProfile>()],
            disabledResolver);

        var disabled = await disabledPolicy.GetRequirementAsync(
            new StepUpAuthorizationContext(
                userId,
                HelloAccountSecurity.AccountDeleteAction,
                "binding"),
            CancellationToken.None);

        Assert.NotNull(disabled);
        Assert.Equal(
            [VerificationMethods.OneTimeCode],
            disabled.AllowedMethods);
    }

    [Fact]
    public async Task UserWithoutAuthenticatorStillNeedsConfirmedContact()
    {
        var user = CreateUserWithoutConfirmedContact();
        var resolver = new HelloStepUpMethodResolver<TestProfile>(
            new HelloDeliveryOptions
            {
                VerificationChannel = HelloDeliveryChannel.Email,
                RequireTotpWhenEnabled = true,
            },
            new UnavailableMessageSender(),
            new FakeTotpService(user.Id, enabled: false));

        var selected = await resolver.SelectAsync(
            user,
            CancellationToken.None);

        Assert.False(selected.IsSuccess);
        Assert.Contains(
            selected.Errors,
            error => error.Code
                == HelloAccountSecurity.ConfirmedEmailRequired().Code);
    }

    private static IdentityUser<TestProfile>
        CreateUserWithoutConfirmedContact()
    {
        var now = DateTimeOffset.UtcNow;
        return new IdentityUser<TestProfile>(
            Guid.NewGuid(),
            UserFlags.None,
            "alice",
            null,
            false,
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
    }

    private sealed record TestProfile(string DisplayName);

    private sealed class FakeTotpService(Guid userId, bool enabled)
        : IIdentityTotpService<TestProfile>
    {
        public Task<OperationResult<TotpFactorStatus>> GetStatusAsync(
            Guid requestedUserId,
            CancellationToken ct)
            => Task.FromResult(
                OperationResultFactory.Success(
                    new TotpFactorStatus(
                        requestedUserId,
                        requestedUserId == userId && enabled,
                        enabled ? 10 : 0,
                        enabled ? DateTimeOffset.UtcNow : null)));

        public Task<OperationResult<TotpEnrollment>> BeginEnrollmentAsync(
            BeginTotpEnrollmentCommand command,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult<ConfirmedTotpEnrollment>>
            ConfirmEnrollmentAsync(
                ConfirmTotpEnrollmentCommand command,
                CancellationToken ct)
            => throw new NotSupportedException();

        public Task<OperationResult> DisableAsync(
            Guid requestedUserId,
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class UnavailableMessageSender
        : IHelloAccountMessageSender
    {
        public OperationResult CheckAvailability(
            HelloDeliveryChannel channel)
            => OperationResultFactory.Fail(
                new Error(
                    HelloDeliveryErrorCodes.NotConfigured,
                    "Delivery is unavailable.",
                    ErrorType.Failure));

        public Task<OperationResult> SendAsync(
            HelloAccountMessage message,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
