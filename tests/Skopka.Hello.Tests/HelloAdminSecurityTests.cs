using Skopka.Hello.Admin;
using Skopka.Identity.StepUp;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Tests;

public sealed class HelloAdminSecurityTests
{
    [Fact]
    public void BindingCoversActorTargetActionAndParameters()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var parameters = new HelloAdminUserActionParameters(
            ExpectedVersion: 12,
            BlockedUntil: DateTimeOffset.UtcNow.AddDays(1),
            Reason: "security review");

        var binding = HelloAdminSecurity.CreateBinding(
            actorId,
            targetId,
            HelloAdminUserAction.Block,
            parameters,
            HelloDeliveryChannel.Email,
            "admin@example.test");

        Assert.Equal(64, binding.Length);
        Assert.Equal(
            binding,
            HelloAdminSecurity.CreateBinding(
                actorId,
                targetId,
                HelloAdminUserAction.Block,
                parameters,
                HelloDeliveryChannel.Email,
                "admin@example.test"));
        Assert.NotEqual(
            binding,
            HelloAdminSecurity.CreateBinding(
                Guid.NewGuid(),
                targetId,
                HelloAdminUserAction.Block,
                parameters,
                HelloDeliveryChannel.Email,
                "admin@example.test"));
        Assert.NotEqual(
            binding,
            HelloAdminSecurity.CreateBinding(
                actorId,
                Guid.NewGuid(),
                HelloAdminUserAction.Block,
                parameters,
                HelloDeliveryChannel.Email,
                "admin@example.test"));
        Assert.NotEqual(
            binding,
            HelloAdminSecurity.CreateBinding(
                actorId,
                targetId,
                HelloAdminUserAction.Delete,
                parameters with { BlockedUntil = null },
                HelloDeliveryChannel.Email,
                "admin@example.test"));
        Assert.NotEqual(
            binding,
            HelloAdminSecurity.CreateBinding(
                actorId,
                targetId,
                HelloAdminUserAction.Block,
                parameters with { ExpectedVersion = 13 },
                HelloDeliveryChannel.Email,
                "admin@example.test"));
        Assert.DoesNotContain(
            "security review",
            binding,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "admin@example.test",
            binding,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HelloAdminSecurity.BlockAction, "hello:admin.user.block")]
    [InlineData(HelloAdminSecurity.UnblockAction, "hello:admin.user.unblock")]
    [InlineData(HelloAdminSecurity.DeleteAction, "hello:admin.user.delete")]
    [InlineData(HelloAdminSecurity.RestoreAction, "hello:admin.user.restore")]
    [InlineData(
        HelloAdminSecurity.RevokeSessionsAction,
        "hello:admin.user.sessions.revoke")]
    public async Task AdminActionsRequireOneTimeCode(
        string action,
        string purpose)
    {
        var provider = new HelloAdminStepUpRequirementProvider<object>();

        var requirement = await provider.GetRequirementAsync(
            new StepUpAuthorizationContext(
                Guid.NewGuid(),
                action,
                "binding"),
            CancellationToken.None);

        Assert.NotNull(requirement);
        Assert.Equal(purpose, requirement.Purpose);
        Assert.Equal(2, requirement.AssuranceLevel);
        Assert.Equal(
            [VerificationMethods.OneTimeCode],
            requirement.AllowedMethods);
    }

    [Fact]
    public void RevokeSessionsRejectsMutationParameters()
    {
        var error = HelloAdminSecurity.Validate(
            Guid.NewGuid(),
            HelloAdminUserAction.RevokeSessions,
            new HelloAdminUserActionParameters(ExpectedVersion: 1));

        Assert.NotNull(error);
        Assert.Equal("identity.validation.failed", error.Code);
    }
}
