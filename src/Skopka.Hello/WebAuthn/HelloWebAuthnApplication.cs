using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.Sessions;
using Skopka.Identity.SignInMethods;
using Skopka.Identity.Users;
using Skopka.Identity.WebAuthn;

namespace Skopka.Hello.WebAuthn;

/// <summary>
/// The two passkey ceremonies as HTTP sees them.
///
/// What is owned here is the challenge and the session. Identity owns the
/// credential: it verifies the response, keeps the key, advances the counter
/// and decides whose it is. This layer issues a challenge, spends it once, and
/// turns a user Identity vouched for into a signed-in session.
/// </summary>
internal sealed class HelloWebAuthnApplication<TProfile>(
    IIdentitySessionService<TProfile> sessions,
    IIdentitySignInMethodQueryService<TProfile> signInMethods,
    IHelloWebAuthnFlowStore flows,
    HelloWebAuthnTickets tickets,
    SkopkaHelloOptions options,
    IIdentityWebAuthnService<TProfile>? credentials = null,
    WebAuthnOptions? webAuthn = null,
    IEnumerable<IHelloAccessTokenValidator<TProfile>>? accessTokenValidators = null)
    : IHelloWebAuthnApplication<TProfile>
{
    public async Task<OperationResult<HelloWebAuthnRegistrationChallenge>>
        BeginRegistrationAsync(
            HelloBeginWebAuthnRegistrationCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Unavailable<HelloWebAuthnRegistrationChallenge>() is { } disabled)
        {
            return disabled;
        }

        var user = await ValidateTokenAsync(command.AccessToken, cancellationToken);
        if (!user.IsSuccess)
        {
            return OperationResultFactory.Fail<
                HelloWebAuthnRegistrationChallenge>(user.Errors);
        }

        var registered = await credentials!.ListAsync(
            user.Value.Id,
            cancellationToken);
        if (!registered.IsSuccess)
        {
            return OperationResultFactory.Fail<
                HelloWebAuthnRegistrationChallenge>(registered.Errors);
        }

        var issued = tickets.Issue(
            HelloWebAuthnCeremony.Registration,
            user.Value.Id,
            options.WebAuthn.ChallengeLifetime);
        return OperationResultFactory.Success(
            new HelloWebAuthnRegistrationChallenge(
                issued.Ticket,
                webAuthn!.RelyingPartyId,
                options.WebAuthn.RelyingPartyName,
                issued.Payload.Challenge,
                // The user handle is the account id and nothing else. A handle
                // an authenticator stores and may show is not a place for an
                // address or a name.
                user.Value.Id.ToByteArray(),
                user.Value.UserName ?? user.Value.Email ?? user.Value.Id.ToString(),
                user.Value.UserName ?? user.Value.Email ?? string.Empty,
                // Offered so an authenticator that already holds a key for this
                // account makes a second one rather than silently replacing it.
                [.. registered.Value.Select(item => item.Id.ToByteArray())],
                HelloWebAuthnAlgorithms.Offered,
                webAuthn!.UserVerificationRequired,
                issued.Payload.ExpiresAt));
    }

    public async Task<OperationResult<HelloWebAuthnCredential>>
        CompleteRegistrationAsync(
            HelloCompleteWebAuthnRegistrationCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Unavailable<HelloWebAuthnCredential>() is { } disabled)
        {
            return disabled;
        }

        var user = await ValidateTokenAsync(command.AccessToken, cancellationToken);
        if (!user.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloWebAuthnCredential>(
                user.Errors);
        }

        var ticket = await SpendAsync(
            command.Ticket,
            HelloWebAuthnCeremony.Registration,
            cancellationToken);
        if (!ticket.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloWebAuthnCredential>(
                ticket.Errors);
        }

        // The ticket names the account it was issued for. Answering one issued
        // for somebody else would register a key against the wrong account.
        if (ticket.Value.UserId != user.Value.Id)
        {
            return Invalid<HelloWebAuthnCredential>();
        }

        var registered = await credentials!.RegisterAsync(
            new RegisterWebAuthnCredentialCommand(
                user.Value.Id,
                command.ClientDataJson,
                command.AttestationObject,
                ticket.Value.Challenge,
                command.Label,
                command.ClientKey),
            cancellationToken);
        return registered.IsSuccess
            ? OperationResultFactory.Success(Describe(registered.Value))
            : OperationResultFactory.Fail<HelloWebAuthnCredential>(
                registered.Errors);
    }

    public Task<OperationResult<HelloWebAuthnAssertionChallenge>> BeginSignInAsync(
        HelloBeginWebAuthnSignInCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (Unavailable<HelloWebAuthnAssertionChallenge>() is { } disabled)
        {
            return Task.FromResult(disabled);
        }

        // No account is named and none is asked for. A challenge handed out
        // before anyone is known cannot say whether an account exists, and a
        // passkey identifies itself.
        var issued = tickets.Issue(
            HelloWebAuthnCeremony.SignIn,
            null,
            options.WebAuthn.ChallengeLifetime);
        return Task.FromResult(OperationResultFactory.Success(
            new HelloWebAuthnAssertionChallenge(
                issued.Ticket,
                webAuthn!.RelyingPartyId,
                issued.Payload.Challenge,
                webAuthn!.UserVerificationRequired,
                issued.Payload.ExpiresAt)));
    }

    public async Task<OperationResult<HelloSignIn<TProfile>>> SignInAsync(
        HelloCompleteWebAuthnSignInCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Unavailable<HelloSignIn<TProfile>>() is { } disabled)
        {
            return disabled;
        }

        var ticket = await SpendAsync(
            command.Ticket,
            HelloWebAuthnCeremony.SignIn,
            cancellationToken);
        if (!ticket.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                ticket.Errors);
        }

        var authenticated = await credentials!.AuthenticateAsync(
            new AuthenticateWebAuthnCommand(
                command.CredentialId,
                command.ClientDataJson,
                command.AuthenticatorData,
                command.Signature,
                ticket.Value.Challenge,
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
            ? OperationResultFactory.Success(new HelloSignIn<TProfile>(
                ToAccount(authenticated.Value),
                ToSession(issued.Value)))
            : OperationResultFactory.Fail<HelloSignIn<TProfile>>(issued.Errors);
    }

    public async Task<OperationResult<IReadOnlyList<HelloWebAuthnCredential>>>
        ListAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (Unavailable<IReadOnlyList<HelloWebAuthnCredential>>() is { } disabled)
        {
            return disabled;
        }

        var user = await ValidateTokenAsync(accessToken, cancellationToken);
        if (!user.IsSuccess)
        {
            return OperationResultFactory.Fail<
                IReadOnlyList<HelloWebAuthnCredential>>(user.Errors);
        }

        var listed = await credentials!.ListAsync(user.Value.Id, cancellationToken);
        return listed.IsSuccess
            ? OperationResultFactory.Success<IReadOnlyList<HelloWebAuthnCredential>>(
                [.. listed.Value.Select(Describe)])
            : OperationResultFactory.Fail<IReadOnlyList<HelloWebAuthnCredential>>(
                listed.Errors);
    }

    public async Task<OperationResult> RemoveAsync(
        HelloRemoveWebAuthnCredentialCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (credentials is null || webAuthn is null)
        {
            return OperationResultFactory.Fail(Disabled());
        }

        var user = await ValidateTokenAsync(command.AccessToken, cancellationToken);
        if (!user.IsSuccess)
        {
            return OperationResultFactory.Fail(user.Errors);
        }

        var last = await IsLastSignInMethodAsync(
            user.Value.Id,
            command.CredentialId,
            cancellationToken);
        if (!last.IsSuccess)
        {
            return OperationResultFactory.Fail(last.Errors);
        }

        if (last.Value)
        {
            // Removing the only way in leaves an account nobody can open,
            // including its owner.
            return OperationResultFactory.Fail(new Error(
                HelloWebAuthnErrorCodes.LastSignInMethod,
                "At least one sign-in method must remain.",
                ErrorType.Conflict));
        }

        return await credentials!.RemoveAsync(
            new RemoveWebAuthnCredentialCommand(
                user.Value.Id,
                command.CredentialId,
                user.Value.Version),
            cancellationToken);
    }

    private async Task<OperationResult<bool>> IsLastSignInMethodAsync(
        Guid userId,
        Guid credentialId,
        CancellationToken cancellationToken)
    {
        var methods = await signInMethods.GetAsync(userId, cancellationToken);
        if (!methods.IsSuccess)
        {
            return OperationResultFactory.Fail<bool>(methods.Errors);
        }

        if (methods.Value.HasPassword || methods.Value.ExternalLogins.Count > 0)
        {
            return OperationResultFactory.Success(false);
        }

        var listed = await credentials!.ListAsync(userId, cancellationToken);
        return listed.IsSuccess
            ? OperationResultFactory.Success(
                !listed.Value.Any(item => item.Id != credentialId))
            : OperationResultFactory.Fail<bool>(listed.Errors);
    }

    /// <summary>
    /// Reads a ticket and spends it. Both halves matter: protection says the
    /// server issued this challenge and has not run out of time, the flow store
    /// says nobody has answered it yet.
    /// </summary>
    private async Task<OperationResult<HelloWebAuthnTicket>> SpendAsync(
        string ticket,
        HelloWebAuthnCeremony ceremony,
        CancellationToken cancellationToken)
    {
        if (tickets.Read(ticket, ceremony) is not { } payload)
        {
            return Invalid<HelloWebAuthnTicket>();
        }

        var spent = await flows.TryConsumeAsync(
            payload.FlowId,
            payload.ExpiresAt,
            cancellationToken);
        return spent
            ? OperationResultFactory.Success(payload)
            : OperationResultFactory.Fail<HelloWebAuthnTicket>(new Error(
                HelloWebAuthnErrorCodes.ChallengeSpent,
                "The challenge has already been answered.",
                ErrorType.Unauthorized));
    }

    /// <summary>
    /// Passkeys are available exactly when the identity builder was told to
    /// support them. Asked of the container rather than of a second flag here:
    /// a flag can be set on a host that never called UseWebAuthn, and the
    /// failure would then be a missing service on the first request rather than
    /// an answer.
    /// </summary>
    private OperationResult<T>? Unavailable<T>()
        => credentials is not null && webAuthn is not null
            ? null
            : OperationResultFactory.Fail<T>(Disabled());

    private static Error Disabled()
        => new(
            HelloWebAuthnErrorCodes.Disabled,
            "Passkeys are not enabled.",
            ErrorType.Forbidden);

    private static OperationResult<T> Invalid<T>()
        => OperationResultFactory.Fail<T>(new Error(
            HelloWebAuthnErrorCodes.ChallengeInvalid,
            "The challenge is invalid or expired.",
            ErrorType.Unauthorized));

    private static HelloWebAuthnCredential Describe(
        WebAuthnCredentialDescriptor credential)
        => new(
            credential.Id,
            credential.Label,
            credential.BackedUp,
            credential.CreatedAt,
            credential.LastUsedAt);

    private async Task<OperationResult<IdentityUser<TProfile>>> ValidateTokenAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        OperationResult<IdentityUser<TProfile>>? firstFailure = null;
        if (accessTokenValidators is not null)
        {
            foreach (var validator in accessTokenValidators)
            {
                var result = await validator.ValidateAsync(
                    accessToken,
                    cancellationToken);
                if (result.IsSuccess)
                {
                    return result;
                }

                firstFailure ??= result;
            }
        }

        return firstFailure
            ?? await sessions.ValidateAccessTokenAsync(
                accessToken,
                cancellationToken);
    }

    private static HelloAccount<TProfile> ToAccount(IdentityUser<TProfile> user)
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
}
