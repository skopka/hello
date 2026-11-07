using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Users;

namespace Skopka.Hello;

/// <summary>Account fields and self-service actions controlled by a host policy.</summary>
public enum HelloAccountArea { UserName, Email, Phone, Profile, Password, Delete }

/// <summary>Controls visibility and self-service mutation independently.</summary>
public sealed record HelloAccountAccess(bool Visible, bool Editable)
{
    /// <summary>Default behavior when a host does not register a policy.</summary>
    public static HelloAccountAccess Allow { get; } = new(true, true);
}

/// <summary>Evaluates the current online account. Hosts must also secure their own APIs.</summary>
public interface IHelloAccountPolicy<TProfile>
{
    /// <summary>Returns current permissions for an account area.</summary>
    Task<OperationResult<HelloAccountAccess>> GetPermissionAsync(
        IdentityUser<TProfile> user, HelloAccountArea area, CancellationToken cancellationToken);

    /// <summary>Checks a proposed profile replacement against the current profile.</summary>
    Task<OperationResult> CheckProfileChangeAsync(
        IdentityUser<TProfile> user, TProfile proposedProfile, CancellationToken cancellationToken);

    /// <summary>Controls whether an individual profile field is shown or edited in the built-in UI.</summary>
    Task<OperationResult<HelloAccountAccess>> GetProfileFieldAccessAsync(
        IdentityUser<TProfile> user, string field, CancellationToken cancellationToken)
        => Task.FromResult(OperationResultFactory.Success(HelloAccountAccess.Allow));

    /// <summary>Checks submitted profile field names before a UI editor consumes them.</summary>
    Task<OperationResult> CheckProfileFieldsAsync(
        IdentityUser<TProfile> user, IReadOnlyCollection<string> fields, CancellationToken cancellationToken);
}

internal static class HelloAccountPolicy
{
    internal static async Task<OperationResult> RequireEditAsync<TProfile>(
        IHelloAccountPolicy<TProfile>? policy, IdentityUser<TProfile> user,
        HelloAccountArea area, CancellationToken cancellationToken)
    {
        if (policy is null) return OperationResultFactory.Success();
        var permission = await policy.GetPermissionAsync(user, area, cancellationToken);
        if (!permission.IsSuccess) return OperationResultFactory.Fail(permission.Errors);
        return permission.Value.Editable ? OperationResultFactory.Success() : OperationResultFactory.Fail(
            new Error("hello.account.action_forbidden", "This account action is unavailable.", ErrorType.Forbidden));
    }
}
