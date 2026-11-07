using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

/// <summary>Host-owned registration continuation and durable onboarding status.</summary>
public interface IHelloRegistrationContinuation<TProfile>
{
    /// <summary>Marks the profile before atomic identity registration.</summary>
    Task<OperationResult<TProfile>> PrepareProfileAsync(
        TProfile profile, HelloRegistrationKind kind, CancellationToken cancellationToken);

    /// <summary>Checks current host-owned onboarding state for the validated account.</summary>
    Task<OperationResult<bool>> IsPendingAsync(
        HelloAccount<TProfile> account, CancellationToken cancellationToken);
}
