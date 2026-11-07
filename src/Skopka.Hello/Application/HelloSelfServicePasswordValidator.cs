using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Credentials;

namespace Skopka.Hello;

internal static class HelloPasswordResetScope
{
    internal static readonly AsyncLocal<bool> Active = new();
}

internal sealed class HelloSelfServicePasswordValidator<TProfile>(IHelloAccountPolicy<TProfile>? policy = null)
    : IPasswordValidator<TProfile>
{
    public Task<OperationResult> ValidateAsync(PasswordValidationContext<TProfile> context, CancellationToken ct)
        => HelloPasswordResetScope.Active.Value && context.Mutation == PasswordMutation.Reset
            ? HelloAccountPolicy.RequireEditAsync(policy, context.User, HelloAccountArea.Password, ct)
            : Task.FromResult(OperationResultFactory.Success());
}
