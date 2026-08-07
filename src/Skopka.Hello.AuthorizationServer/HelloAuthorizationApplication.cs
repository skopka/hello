using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;

namespace Skopka.Hello.AuthorizationServer;

internal sealed record HelloAuthorizationSubject(
    Guid UserId,
    Guid SessionId,
    IReadOnlyList<IdentitySessionClaim> Claims);

internal interface IHelloAuthorizationApplication<TProfile>
{
    Task<OperationResult<HelloAuthorizationSubject>> ValidateAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloAuthorizationSubject>> CreateAsync(
        Guid userId,
        Guid sourceSessionId,
        string clientName,
        CancellationToken cancellationToken);
}

internal sealed class HelloAuthorizationApplication<TProfile>(
    IIdentitySessionRegistry<TProfile> sessions,
    IEnumerable<IIdentitySessionClaimsProvider<TProfile>> claimsProviders)
    : IHelloAuthorizationApplication<TProfile>
{
    public async Task<OperationResult<HelloAuthorizationSubject>> ValidateAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var validated = await sessions.ValidateAsync(
            new ValidateIdentitySessionCommand(userId, sessionId),
            cancellationToken);
        return validated.IsSuccess
            ? OperationResultFactory.Success(
                new HelloAuthorizationSubject(
                    validated.Value.Id,
                    sessionId,
                    await ProjectClaimsAsync(
                        validated.Value,
                        cancellationToken)))
            : OperationResultFactory.Fail<HelloAuthorizationSubject>(
                validated.Errors);
    }

    public async Task<OperationResult<HelloAuthorizationSubject>> CreateAsync(
        Guid userId,
        Guid sourceSessionId,
        string clientName,
        CancellationToken cancellationToken)
    {
        var source = await sessions.ValidateAsync(
            new ValidateIdentitySessionCommand(userId, sourceSessionId),
            cancellationToken);
        if (!source.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAuthorizationSubject>(
                source.Errors);
        }

        var registered = await sessions.RegisterAsync(
            new RegisterIdentitySessionCommand(
                source.Value.Id,
                source.Value.SecurityStamp,
                new IdentitySessionMetadata(clientName)),
            cancellationToken);
        return registered.IsSuccess
            ? OperationResultFactory.Success(
                new HelloAuthorizationSubject(
                    source.Value.Id,
                    registered.Value.SessionId,
                    await ProjectClaimsAsync(
                        source.Value,
                        cancellationToken)))
            : OperationResultFactory.Fail<HelloAuthorizationSubject>(
                registered.Errors);
    }

    private async Task<IReadOnlyList<IdentitySessionClaim>> ProjectClaimsAsync(
        IdentityUser<TProfile> user,
        CancellationToken cancellationToken)
    {
        var claims = new List<IdentitySessionClaim>();
        foreach (var provider in claimsProviders)
        {
            var projected = await provider.GetClaimsAsync(
                    user,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "An Identity session claims provider returned null.");
            claims.AddRange(projected);
            if (claims.Count > IdentitySessionClaimLimits.MaximumClaimCount)
            {
                throw new InvalidOperationException(
                    "Authorization claims exceed the supported count.");
            }
        }

        foreach (var claim in claims)
        {
            if (claim is null
                || string.IsNullOrWhiteSpace(claim.Type)
                || claim.Type.Length
                    > IdentitySessionClaimLimits.MaximumTypeLength
                || claim.Value is null
                || claim.Value.Length
                    > IdentitySessionClaimLimits.MaximumValueLength)
            {
                throw new InvalidOperationException(
                    "An authorization claim is invalid.");
            }
        }

        return claims;
    }
}
