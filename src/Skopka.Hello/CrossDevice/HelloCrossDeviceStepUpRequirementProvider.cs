using Skopka.Identity.DeviceAuthorization;
using Skopka.Identity.StepUp;

namespace Skopka.Hello;

internal sealed class HelloCrossDeviceStepUpRequirementProvider<TProfile>(
    HelloCrossDeviceSignInOptions options)
    : IHelloStepUpRequirementProvider<TProfile>
{
    public Task<StepUpRequirement?> GetRequirementAsync(
        StepUpAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<StepUpRequirement?>(
            string.Equals(
                context.Action,
                DeviceAuthorizationActions.Approve,
                StringComparison.Ordinal)
                ? new StepUpRequirement(
                    "hello:device_authorization.approve",
                    [options.StepUpMethod],
                    AssuranceLevel: 2,
                    MaximumAge: options.StepUpMaximumAge)
                : null);
    }
}
