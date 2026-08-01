using Skopka.Abstraction.OperationResult;
using Skopka.Identity.ExternalLogins;
using Skopka.Identity.Sessions;
using Skopka.Identity.SignInMethods;

namespace Skopka.Hello;

public static class HelloExternalIdentityErrorCodes
{
    public const string RestartRequired =
        "hello.account.external_mutation_restart_required";
}

public sealed record HelloExternalSignInCommand(
    ExternalLoginKey Login,
    IdentitySessionMetadata SessionMetadata);

public sealed record HelloExternalRegistrationCommand<TProfile>(
    string? UserName,
    string? Email,
    string? Phone,
    TProfile Profile,
    ExternalLoginKey Login,
    IdentitySessionMetadata SessionMetadata);

public sealed record HelloBeginExternalLoginMutationCommand(
    string AccessToken,
    ExternalLoginKey Login,
    string? ClientKey);

public sealed record HelloCompleteExternalLoginMutationCommand(
    string AccessToken,
    ExternalLoginKey Login,
    long ExpectedVersion,
    Guid ChallengeId,
    string VerificationCode,
    IdentitySessionMetadata SessionMetadata);

public interface IHelloExternalIdentityApplication<TProfile>
{
    Task<OperationResult<HelloSignIn<TProfile>>> SignInAsync(
        HelloExternalSignInCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloSignIn<TProfile>>> RegisterAsync(
        HelloExternalRegistrationCommand<TProfile> command,
        CancellationToken cancellationToken);

    Task<OperationResult<SignInMethodSnapshot>> GetSignInMethodsAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginLinkAsync(
        HelloBeginExternalLoginMutationCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloSignIn<TProfile>>> CompleteLinkAsync(
        HelloCompleteExternalLoginMutationCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginUnlinkAsync(
        HelloBeginExternalLoginMutationCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloSignIn<TProfile>>> CompleteUnlinkAsync(
        HelloCompleteExternalLoginMutationCommand command,
        CancellationToken cancellationToken);
}
