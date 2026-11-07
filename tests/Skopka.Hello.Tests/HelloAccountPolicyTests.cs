using Skopka.Abstraction.OperationResult;
using Skopka.Hello;
using Skopka.Identity.Credentials;
using Skopka.Identity.Users;

namespace Skopka.Hello.Tests;

public sealed class HelloAccountPolicyTests
{
    private sealed record Profile;
    private sealed class DenyPassword : IHelloAccountPolicy<Profile>
    {
        public Task<OperationResult<HelloAccountAccess>> GetPermissionAsync(IdentityUser<Profile> user, HelloAccountArea area, CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success(area == HelloAccountArea.Password
                ? new HelloAccountAccess(true, false) : HelloAccountAccess.Allow));
        public Task<OperationResult> CheckProfileChangeAsync(IdentityUser<Profile> user, Profile profile, CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success());
        public Task<OperationResult> CheckProfileFieldsAsync(IdentityUser<Profile> user, IReadOnlyCollection<string> fields, CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success());
    }
    [Fact]
    public async Task SelfServiceResetValidatorDeniesOnlyScopedReset()
    {
        var user = new IdentityUser<Profile>(Guid.NewGuid(), default, "child", null, false, null, false,
            new Profile(), 1, "stamp", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var validator = new HelloSelfServicePasswordValidator<Profile>(new DenyPassword());
        var context = new PasswordValidationContext<Profile>(user, "password", PasswordMutation.Reset);
        var direct = await validator.ValidateAsync(context, CancellationToken.None);
        Assert.True(direct.IsSuccess);
        HelloPasswordResetScope.Active.Value = true;
        try
        {
            var result = await validator.ValidateAsync(context, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal("hello.account.action_forbidden", Assert.Single(result.Errors).Code);
        }
        finally { HelloPasswordResetScope.Active.Value = false; }
    }
}
