using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;

namespace Skopka.Hello;

internal sealed class IdentityAccessTokenValidator<TProfile>(
    IIdentitySessionService<TProfile> sessions)
    : IHelloAccessTokenValidator<TProfile>
{
    public Task<OperationResult<IdentityUser<TProfile>>> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken)
        => sessions.ValidateAccessTokenAsync(
            accessToken,
            cancellationToken);
}
