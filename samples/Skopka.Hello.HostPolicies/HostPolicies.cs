using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;

namespace Skopka.Hello.HostPolicies;

public enum AccountKind { Personal, Child, Staff }
public sealed record Profile(string DisplayName, AccountKind Kind, bool OnboardingPending);

/// <summary>Compiled policy example; the host owns provisioning and the onboarding form.</summary>
public static class PolicyRegistration
{
    public static void AddExamplePolicies(this IServiceCollection services)
    {
        services.AddScoped<IHelloSignInPolicy<Profile>, SignInPolicy>();
        services.AddScoped<IHelloSignInSecondFactorPolicy<Profile>, StaffSecondFactor>();
        services.AddScoped<IHelloAccountPolicy<Profile>, AccountPolicy>();
        services.AddScoped<IHelloRegistrationContinuation<Profile>, Continuation>();
        services.AddScoped<IIdentitySessionLifetimePolicy<Profile>, SessionLifetime>();
        services.AddScoped<IHelloDelegatedPhoneRegistrationPolicy<Profile>, DelegatedRegistration>();
    }
}

public sealed class SignInPolicy : IHelloSignInPolicy<Profile>
{
    public Task<bool> IsAllowedAsync(HelloSignInContext<Profile> context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(context.Account.Profile.Kind switch
        {
            AccountKind.Staff => context.Site.Id == "staff" && context.AuthenticationMethod == HelloAuthenticationMethods.Password,
            AccountKind.Child => context.Site.Id == "personal" && context.AuthenticationMethod is HelloAuthenticationMethods.Password or HelloAuthenticationMethods.Phone,
            AccountKind.Personal => context.Site.Id == "personal" && context.AuthenticationMethod == HelloAuthenticationMethods.Phone,
            _ => false
        });
    }
}

public sealed class StaffSecondFactor : IHelloSignInSecondFactorPolicy<Profile>
{
    public Task<bool> RequiresAsync(IdentityUser<Profile> user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(user.Profile.Kind == AccountKind.Staff);
    }
}

public sealed class SessionLifetime : IIdentitySessionLifetimePolicy<Profile>
{
    public Task<TimeSpan> GetLifetimeAsync(IdentityUser<Profile> user, IdentitySessionContext? context,
        TimeSpan defaultLifetime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(user.Profile.Kind switch
        {
            AccountKind.Child => TimeSpan.FromDays(180),
            AccountKind.Staff => TimeSpan.FromHours(8),
            _ => defaultLifetime
        });
    }
}

public sealed class Continuation : IHelloRegistrationContinuation<Profile>
{
    public Task<OperationResult<Profile>> PrepareProfileAsync(Profile profile, HelloRegistrationKind kind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Never let anonymous profile JSON provision a staff account or skip onboarding.
        return Task.FromResult(OperationResultFactory.Success(profile with
        {
            Kind = kind == HelloRegistrationKind.DelegatedPhoneOtp ? AccountKind.Child : AccountKind.Personal,
            OnboardingPending = true
        }));
    }

    public Task<OperationResult<bool>> IsPendingAsync(HelloAccount<Profile> account, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResultFactory.Success(account.Profile.OnboardingPending));
    }
}

public sealed class AccountPolicy : IHelloAccountPolicy<Profile>
{
    private static OperationResult Forbidden() => OperationResultFactory.Fail(new Error(
        "host.profile.forbidden", "This profile change is unavailable.", ErrorType.Forbidden));

    public Task<OperationResult<HelloAccountAccess>> GetPermissionAsync(IdentityUser<Profile> user,
        HelloAccountArea area, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HelloAccountAccess access = (user.Profile.Kind, area) switch
        {
            (AccountKind.Staff, HelloAccountArea.Phone or HelloAccountArea.Delete) => new(false, false),
            (AccountKind.Staff, HelloAccountArea.UserName or HelloAccountArea.Profile) => new(true, false),
            (AccountKind.Child, HelloAccountArea.Phone or HelloAccountArea.Email or HelloAccountArea.Password or HelloAccountArea.Delete) => new(false, false),
            _ => HelloAccountAccess.Allow
        };
        return Task.FromResult(OperationResultFactory.Success(access));
    }

    public Task<OperationResult> CheckProfileChangeAsync(IdentityUser<Profile> user, Profile proposed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The host's authenticated onboarding transaction uses Identity directly.
        // Self-service profile replacement cannot change server-owned state.
        return Task.FromResult(proposed.Kind != user.Profile.Kind
            || proposed.OnboardingPending != user.Profile.OnboardingPending
            || user.Profile.Kind == AccountKind.Staff && proposed != user.Profile
                ? Forbidden() : OperationResultFactory.Success());
    }

    public Task<OperationResult<HelloAccountAccess>> GetProfileFieldAccessAsync(IdentityUser<Profile> user,
        string field, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResultFactory.Success(field == "displayName"
            ? new HelloAccountAccess(true, user.Profile.Kind != AccountKind.Staff)
            : new HelloAccountAccess(false, false)));
    }

    public Task<OperationResult> CheckProfileFieldsAsync(IdentityUser<Profile> user,
        IReadOnlyCollection<string> fields, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(fields.Any(field => field != "displayName")
            || user.Profile.Kind == AccountKind.Staff && fields.Count != 0
                ? Forbidden() : OperationResultFactory.Success());
    }
}

public sealed class DelegatedRegistration : IHelloDelegatedPhoneRegistrationPolicy<Profile>
{
    public Task<OperationResult> AuthorizeAsync(HelloAccount<Profile> actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(actor.Profile.Kind == AccountKind.Personal && !actor.Profile.OnboardingPending
            ? OperationResultFactory.Success()
            : OperationResultFactory.Fail(new Error("host.child_registration.forbidden", "This action is unavailable.", ErrorType.Forbidden)));
    }
}
