using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Sessions;
using Skopka.Identity.Errors;
using Skopka.Identity.Users;

namespace Skopka.Hello;

internal sealed class IdentityAccessTokenValidator<TProfile>(
    IIdentitySessionService<TProfile> sessions,
    IIdentityAccessTokenProvider? tokens = null,
    IIdentitySessionRegistry<TProfile>? registry = null)
    : IHelloAccessTokenValidator<TProfile>
{
    public Task<OperationResult<IdentityUser<TProfile>>> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken)
        => sessions.ValidateAccessTokenAsync(
            accessToken,
            cancellationToken);

    public async Task<OperationResult<IdentityValidatedSession<TProfile>>> ValidateWithAssuranceAsync(
        string accessToken, CancellationToken cancellationToken)
    {
        var user = await sessions.ValidateAccessTokenAsync(accessToken, cancellationToken);
        if (!user.IsSuccess) return OperationResultFactory.Fail<IdentityValidatedSession<TProfile>>(user.Errors);
        if (tokens is null || registry is null)
            return OperationResultFactory.Success(new IdentityValidatedSession<TProfile>(user.Value, IdentitySessionAssurance.None));
        var payload = await tokens.ValidateAsync(accessToken, cancellationToken);
        if (payload is null || payload.UserId != user.Value.Id)
            return OperationResultFactory.Fail<IdentityValidatedSession<TProfile>>(new Error(IdentityErrorCodes.AccessTokenInvalid, "The access token is invalid.", ErrorType.Unauthorized));
        return await registry.ValidateWithAssuranceAsync(new ValidateIdentitySessionCommand(payload.UserId, payload.SessionId), cancellationToken);
    }
}
