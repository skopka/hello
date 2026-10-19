using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Authentication;
using Skopka.Identity.Credentials;
using Skopka.Identity.Errors;
using Skopka.Identity.Users;

namespace Skopka.Hello;

internal sealed class UnavailablePasswordServices<TProfile>
    : IPasswordAuthenticationService<TProfile>,
      IPasswordCredentialService<TProfile>
{
    private static readonly Error Unavailable = new(
        IdentityErrorCodes.InvalidCredentials,
        "Password sign-in is unavailable.",
        ErrorType.Unauthorized);

    public Task<OperationResult<IdentityUser<TProfile>>> AuthenticateAsync(
        AuthenticatePasswordCommand cmd,
        CancellationToken ct)
        => Task.FromResult(
            OperationResultFactory.Fail<IdentityUser<TProfile>>(Unavailable));

    public Task<OperationResult> SetPasswordAsync(
        SetPasswordCommand cmd, CancellationToken ct)
        => Failed();

    public Task<OperationResult> ChangePasswordAsync(
        ChangePasswordCommand cmd, CancellationToken ct)
        => Failed();

    public Task<OperationResult> RemovePasswordAsync(
        RemovePasswordCommand cmd, CancellationToken ct)
        => Failed();

    public Task<OperationResult> ResetPasswordAsync(
        ResetPasswordCommand cmd, CancellationToken ct)
        => Failed();

    public Task<OperationResult> VerifyPasswordAsync(
        VerifyPasswordCommand cmd, CancellationToken ct)
        => Failed();

    private static Task<OperationResult> Failed()
        => Task.FromResult(OperationResultFactory.Fail(Unavailable));
}
