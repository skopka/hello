using Skopka.Abstraction.OperationResult;
using Skopka.Hello.AuthorizationServer;

namespace Skopka.Hello.Admin;

public sealed record HelloAdminClientAction(
    string Action,
    HelloAuthorizationClientInput Input,
    long? ExpectedRevision);

public sealed record HelloAdminClientCompletion(
    HelloAdminClientAction Action,
    Guid ChallengeId,
    string VerificationCode);

public interface IHelloAdminClientApplication
{
    Task<OperationResult<IReadOnlyList<HelloAuthorizationClientView>>> ListAsync(
        string token,
        int offset,
        int count,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginAsync(
        string token,
        HelloAdminClientAction action,
        string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloAuthorizationClientMutation>> CompleteAsync(
        string token,
        HelloAdminClientCompletion completion,
        string? clientKey,
        CancellationToken cancellationToken);
}
