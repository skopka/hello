using Skopka.Identity.StepUp;

namespace Skopka.Hello;

/// <summary>
/// Contributes application-owned step-up requirements to the single
/// Skopka.Identity policy provider composed by Skopka.Hello.
/// </summary>
public interface IHelloStepUpRequirementProvider<TProfile>
{
    Task<StepUpRequirement?> GetRequirementAsync(
        StepUpAuthorizationContext context,
        CancellationToken cancellationToken);
}
