using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Authentication;
using Skopka.Identity.Registration;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Commands;

namespace Skopka.Hello;

internal sealed class HelloIdentityApplication<TProfile>(
    IIdentityRegistrationService<TProfile> registration,
    IPasswordAuthenticationService<TProfile> authentication,
    IIdentitySessionService<TProfile> sessions)
    : IHelloIdentityApplication<TProfile>
{
    public async Task<OperationResult<HelloAccount<TProfile>>> RegisterAsync(
        HelloRegisterCommand<TProfile> command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await registration.RegisterPasswordAsync(
            new RegisterPasswordUserCommand<TProfile>(
                new CreateUserCommand<TProfile>(
                    command.UserName,
                    command.Email,
                    command.Phone,
                    command.Profile),
                command.Password),
            cancellationToken);

        return result.IsSuccess
            ? OperationResultFactory.Success(ToAccount(result.Value))
            : OperationResultFactory.Fail<HelloAccount<TProfile>>(
                result.Errors);
    }

    public async Task<OperationResult<HelloSignIn<TProfile>>> LoginAsync(
        HelloLoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var authenticated = await authentication.AuthenticateAsync(
            new AuthenticatePasswordCommand(
                command.Handle,
                command.Login,
                command.Password,
                command.ClientKey),
            cancellationToken);
        if (!authenticated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                authenticated.Errors);
        }

        var issued = await sessions.CreateAsync(
            new CreateIdentitySessionCommand(
                authenticated.Value.Id,
                authenticated.Value.SecurityStamp,
                command.SessionMetadata),
            cancellationToken);
        return issued.IsSuccess
            ? OperationResultFactory.Success(
                new HelloSignIn<TProfile>(
                    ToAccount(authenticated.Value),
                    ToSession(issued.Value)))
            : OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                issued.Errors);
    }

    public async Task<OperationResult<HelloSession>> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var result = await sessions.RefreshAsync(
            new RefreshIdentitySessionCommand(refreshToken),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(ToSession(result.Value))
            : OperationResultFactory.Fail<HelloSession>(result.Errors);
    }

    public async Task<OperationResult<HelloAccount<TProfile>>>
        ValidateAccessTokenAsync(
            string accessToken,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var result = await sessions.ValidateAccessTokenAsync(
            accessToken,
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(ToAccount(result.Value))
            : OperationResultFactory.Fail<HelloAccount<TProfile>>(
                result.Errors);
    }

    public Task<OperationResult> LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        return sessions.RevokeAsync(
            new RevokeIdentitySessionCommand(refreshToken),
            cancellationToken);
    }

    public Task<OperationResult> LogoutAllAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => sessions.RevokeAllAsync(
            new RevokeAllIdentitySessionsCommand(userId),
            cancellationToken);

    public async Task<OperationResult<IReadOnlyList<HelloSessionInfo>>>
        ListSessionsAsync(
            Guid userId,
            CancellationToken cancellationToken)
    {
        var result = await sessions.ListAsync(
            new ListIdentitySessionsCommand(userId),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success<IReadOnlyList<HelloSessionInfo>>(
                result.Value.Select(ToSessionInfo).ToArray())
            : OperationResultFactory.Fail<IReadOnlyList<HelloSessionInfo>>(
                result.Errors);
    }

    public Task<OperationResult> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
        => sessions.RevokeByIdAsync(
            new RevokeIdentitySessionByIdCommand(userId, sessionId),
            cancellationToken);

    private static HelloAccount<TProfile> ToAccount(
        IdentityUser<TProfile> user)
        => new(
            user.Id,
            user.Flags,
            user.UserName,
            user.Email,
            user.EmailConfirmed,
            user.Phone,
            user.PhoneConfirmed,
            user.Profile,
            user.Version,
            user.CreatedAt,
            user.ModifiedAt);

    private static HelloSession ToSession(IssuedIdentitySession session)
        => new(
            session.SessionId,
            session.AccessToken,
            session.AccessTokenExpiresAt,
            session.RefreshToken,
            session.RefreshTokenExpiresAt);

    private static HelloSessionInfo ToSessionInfo(
        IdentitySessionInfo session)
        => new(
            session.SessionId,
            session.Metadata.ClientName,
            session.Metadata.DeviceName,
            session.ExpiresAt,
            session.CreatedAt,
            session.LastRefreshedAt);
}
