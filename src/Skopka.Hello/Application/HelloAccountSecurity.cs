using Skopka.Abstraction.OperationResult;
using Skopka.Identity.StepUp;
using Skopka.Identity.Verification;

namespace Skopka.Hello;

internal static class HelloAccountSecurity
{
    public const string PasswordChangeAction =
        "account.password.change";

    public const string PasswordChangePurpose =
        "hello:account.password.change";

    public static string CreateBinding(Guid userId)
        => userId.ToString("D");

    public static Error ConfirmedEmailRequired()
        => new(
            "hello.account.confirmed_email_required",
            "A confirmed email address is required for this action.",
            ErrorType.Forbidden);
}

internal sealed class HelloStepUpPolicyProvider<TProfile>
    : IStepUpPolicyProvider<TProfile>
{
    private static readonly StepUpRequirement PasswordChange =
        new(
            HelloAccountSecurity.PasswordChangePurpose,
            [VerificationMethods.OneTimeCode],
            AssuranceLevel: 2,
            MaximumAge: TimeSpan.FromMinutes(2));

    public Task<StepUpRequirement?> GetRequirementAsync(
        StepUpAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<StepUpRequirement?>(
            string.Equals(
                context.Action,
                HelloAccountSecurity.PasswordChangeAction,
                StringComparison.Ordinal)
                ? PasswordChange
                : null);
    }
}
