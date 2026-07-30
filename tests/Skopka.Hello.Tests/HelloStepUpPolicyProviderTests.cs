using Skopka.Identity.StepUp;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Tests;

public sealed class HelloStepUpPolicyProviderTests
{
    [Fact]
    public async Task PasswordChangeRequiresOneTimeCode()
    {
        var provider =
            new HelloStepUpPolicyProvider<object>();

        var requirement = await provider.GetRequirementAsync(
            new StepUpAuthorizationContext(
                Guid.NewGuid(),
                HelloAccountSecurity.PasswordChangeAction,
                Guid.NewGuid().ToString("D")),
            CancellationToken.None);

        Assert.NotNull(requirement);
        Assert.Equal(
            HelloAccountSecurity.PasswordChangePurpose,
            requirement.Purpose);
        Assert.Equal(2, requirement.AssuranceLevel);
        Assert.Equal(
            TimeSpan.FromMinutes(2),
            requirement.MaximumAge);
        Assert.Equal(
            [VerificationMethods.OneTimeCode],
            requirement.AllowedMethods);
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
