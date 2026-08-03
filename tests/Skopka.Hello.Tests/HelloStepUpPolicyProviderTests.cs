using Skopka.Identity.StepUp;
using Skopka.Identity.ExternalLogins;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Tests;

public sealed class HelloStepUpPolicyProviderTests
{
    [Theory]
    [InlineData(
        HelloAccountSecurity.PasswordChangeAction,
        HelloAccountSecurity.PasswordChangePurpose)]
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
}
