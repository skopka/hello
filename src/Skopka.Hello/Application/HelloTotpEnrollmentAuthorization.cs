using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.WebAuthn;
using Skopka.Identity.Credentials;
using Skopka.Identity.Errors;
using Skopka.Identity.ExternalLogins;
using Skopka.Identity.PhoneOtp;
using Skopka.Identity.RateLimiting;
using Skopka.Identity.Sessions;
using Skopka.Identity.SignInMethods;
using Skopka.Identity.Totp;
using Skopka.Identity.Users;
using Skopka.Identity.WebAuthn;

namespace Skopka.Hello;

internal sealed record HelloTotpEnrollmentTicket(
    string Ticket,
    DateTimeOffset ExpiresAt);

internal sealed class HelloTotpEnrollmentAuthorization<TProfile>(
    IDataProtectionProvider protection,
    IHelloWebAuthnFlowStore flows,
    IIdentitySessionService<TProfile> sessions,
    IIdentitySignInMethodQueryService<TProfile> methods,
    IPasswordCredentialService<TProfile> passwords,
    IHelloPhoneOtpApplication<TProfile> phoneApplication,
    IIdentityPhoneOtpService<TProfile> phone,
    SkopkaHelloOptions options,
    IEnumerable<IHelloAccessTokenValidator<TProfile>> validators,
    IEnumerable<IIdentityRateLimiter<TProfile>> limiters,
    HelloTotpPasswordAttempts fallback,
    TimeProvider time,
    IIdentityWebAuthnService<TProfile>? passkeys = null,
    WebAuthnOptions? webAuthn = null,
    IIdentityTotpService<TProfile>? totp = null)
    : IHelloTotpEnrollmentAuthorization<TProfile>
{
    internal const string AuthorizationInvalidCode =
        "hello.totp.authorization_invalid";

    private readonly IDataProtector protector = protection.CreateProtector(
        "Skopka.Hello.TotpEnrollmentAuthorization.v1");
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private sealed record Ticket(
        Guid FlowId,
        string Kind,
        Guid UserId,
        string TokenHash,
        long Version,
        DateTimeOffset ExpiresAt,
        string? Data = null,
        Guid Id = default);

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private string Seal(Ticket value)
        => protector.Protect(JsonSerializer.Serialize(value));

    private Ticket? Read(
        string? value, string kind, string token, Guid userId, long version)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8192)
        {
            return null;
        }

        try
        {
            var ticket = JsonSerializer.Deserialize<Ticket>(protector.Unprotect(value));
            return ticket is not null
                && ticket.Kind == kind
                && ticket.UserId == userId
                && ticket.TokenHash == Hash(token)
                && ticket.Version == version
                && ticket.ExpiresAt > time.GetUtcNow()
                && ticket.FlowId != Guid.Empty
                ? ticket
                : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    internal static Error Invalid()
        => new(AuthorizationInvalidCode, "Fresh authentication is required.", ErrorType.Unauthorized);

    private static OperationResult<T> Fail<T>()
        => OperationResultFactory.Fail<T>(Invalid());

    private async Task<OperationResult<IdentityUser<TProfile>>> Validate(string token, CancellationToken ct)
    {
        OperationResult<IdentityUser<TProfile>>? firstFailure = null;
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(token, ct);
            if (result.IsSuccess)
            {
                return result;
            }
            firstFailure ??= result;
        }

        return firstFailure ?? await sessions.ValidateAccessTokenAsync(token, ct);
    }
    private async Task<(IdentityUser<TProfile>? User, SignInMethodSnapshot? Methods)> Context(
        string token, CancellationToken ct)
    {
        if (!options.Totp.Enabled || totp is null)
        {
            return (null, null);
        }

        var user = await Validate(token, ct);
        if (!user.IsSuccess)
        {
            return (null, null);
        }

        var snapshot = await methods.GetAsync(user.Value.Id, ct);
        return snapshot.IsSuccess ? (user.Value, snapshot.Value) : (null, null);
    }

    private Ticket New(
        string kind, string token, Guid userId, long version, string? data = null, Guid id = default)
        => new(Guid.NewGuid(), kind, userId, Hash(token), version,
            time.GetUtcNow().Add(Lifetime), data, id);

    private async Task<bool> Spend(Ticket ticket, CancellationToken ct)
        => await flows.TryConsumeAsync(ticket.FlowId, ticket.ExpiresAt, ct);

    internal async Task<OperationResult<long>> AuthorizeAsync(
        HelloBeginTotpEnrollmentCommand command, CancellationToken ct)
    {
        var (user, snapshot) = await Context(command.AccessToken, ct);
        if (user is null || snapshot is null)
        {
            return Fail<long>();
        }

        if (snapshot.HasPassword)
        {
            var limiter = limiters.FirstOrDefault();
            var accountKey = Hash(user.Id.ToString("N"));
            var clientKey = Hash(command.ClientKey ?? "unknown");
            var allowed = limiter is null
                ? fallback.TryHit("account:" + accountKey, 5)
                    && fallback.TryHit("client:" + clientKey, 20)
                : (await limiter.HitAsync(new RateLimitRequest(
                    "hello.totp.password.account", accountKey, 5, TimeSpan.FromMinutes(5)), ct)).IsAllowed
                    && (await limiter.HitAsync(new RateLimitRequest(
                        "hello.totp.password.client", clientKey, 20, TimeSpan.FromMinutes(5)), ct)).IsAllowed;
            if (!allowed)
            {
                return OperationResultFactory.Fail<long>(new Error(
                    IdentityErrorCodes.RateLimitExceeded,
                    "Too many requests.",
                    ErrorType.Forbidden));
            }

            if (string.IsNullOrEmpty(command.Password))
            {
                return Fail<long>();
            }

            var verified = await passwords.VerifyPasswordAsync(
                new VerifyPasswordCommand(user.Id, command.Password), ct);
            return verified.IsSuccess
                ? OperationResultFactory.Success(snapshot.Version)
                : OperationResultFactory.Fail<long>(verified.Errors);
        }

        var proof = Read(command.Proof, "proof", command.AccessToken, user.Id, snapshot.Version);
        return proof is not null && await Spend(proof, ct)
            ? OperationResultFactory.Success(snapshot.Version)
            : Fail<long>();
    }

    internal async Task<HelloTotpEnrollmentTicket?> IssueEnrollmentTicketAsync(
        string token,
        Guid enrollmentId,
        DateTimeOffset expiresAt,
        long expectedVersion,
        CancellationToken ct)
    {
        var (user, snapshot) = await Context(token, ct);
        // Beginning this exact pending enrollment advances the user version once.
        // Any concurrent mutation adds another increment and invalidates the proof.
        if (user is null || snapshot is null || expectedVersion == long.MaxValue
            || snapshot.Version != expectedVersion + 1)
        {
            return null;
        }

        var ticket = New("enrollment", token, user.Id, snapshot.Version, id: enrollmentId);
        ticket = ticket with
        {
            ExpiresAt = expiresAt < ticket.ExpiresAt ? expiresAt : ticket.ExpiresAt,
        };
        return new HelloTotpEnrollmentTicket(Seal(ticket), ticket.ExpiresAt);
    }

    internal async Task<bool> ValidateEnrollmentTicketAsync(
        string token, string? value, Guid enrollmentId, CancellationToken ct)
    {
        var (user, snapshot) = await Context(token, ct);
        if (user is null || snapshot is null)
        {
            return false;
        }

        var ticket = Read(value, "enrollment", token, user.Id, snapshot.Version);
        // Identity owns the atomic pending-to-enabled transition. The ticket
        // remains usable for retrying an incorrect authenticator code.
        return ticket is not null && ticket.Id == enrollmentId;
    }
    public async Task<OperationResult<HelloTotpPhoneChallenge>> BeginPhoneAsync(
        string accessToken, string? clientKey, CancellationToken cancellationToken)
    {
        var (user, snapshot) = await Context(accessToken, cancellationToken);
        if (user is null || snapshot is null || snapshot.HasPassword
            || !snapshot.HasPhoneOtp || !options.PhoneOtpEnabled
            || !user.PhoneConfirmed || string.IsNullOrWhiteSpace(user.Phone))
        {
            return Fail<HelloTotpPhoneChallenge>();
        }

        var sent = await phoneApplication.RequestCodeAsync(
            user.Phone, clientKey, null, cancellationToken);
        if (!sent.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloTotpPhoneChallenge>(sent.Errors);
        }

        var ticket = New(
            "phone", accessToken, user.Id, snapshot.Version, id: sent.Value.RequestId);
        return OperationResultFactory.Success(
            new HelloTotpPhoneChallenge(Seal(ticket), ticket.ExpiresAt));
    }

    public async Task<OperationResult<string>> CompletePhoneAsync(
        string accessToken, string ticket, string code, string? clientKey,
        CancellationToken cancellationToken)
    {
        var (user, snapshot) = await Context(accessToken, cancellationToken);
        if (user is null || snapshot is null || snapshot.HasPassword
            || !snapshot.HasPhoneOtp || !options.PhoneOtpEnabled || !user.PhoneConfirmed)
        {
            return Fail<string>();
        }

        var payload = Read(ticket, "phone", accessToken, user.Id, snapshot.Version);
        if (payload is null)
        {
            return Fail<string>();
        }

        var verified = await phone.VerifyAsync(
            new VerifyPhoneOtpCommand(payload.Id, code, clientKey), cancellationToken);
        if (!verified.IsSuccess)
        {
            return OperationResultFactory.Fail<string>(verified.Errors);
        }

        if (verified.Value.User?.Id != user.Id || verified.Value.Phone != user.Phone
            || !await Spend(payload, cancellationToken))
        {
            return Fail<string>();
        }

        var current = await Context(accessToken, cancellationToken);
        if (current.User?.Id != user.Id || current.Methods?.Version != snapshot.Version)
        {
            return Fail<string>();
        }

        return OperationResultFactory.Success(
            Seal(New("proof", accessToken, user.Id, snapshot.Version)));
    }

    public async Task<OperationResult<HelloWebAuthnAssertionChallenge>> BeginPasskeyAsync(
        string accessToken, string? clientKey, CancellationToken cancellationToken)
    {
        var (user, snapshot) = await Context(accessToken, cancellationToken);
        if (user is null || snapshot is null || snapshot.HasPassword
            || passkeys is null || webAuthn is null)
        {
            return Fail<HelloWebAuthnAssertionChallenge>();
        }

        var listed = await passkeys.ListAsync(user.Id, cancellationToken);
        if (!listed.IsSuccess || listed.Value.Count == 0)
        {
            return Fail<HelloWebAuthnAssertionChallenge>();
        }

        var challenge = RandomNumberGenerator.GetBytes(32);
        var ticket = New(
            "passkey", accessToken, user.Id, snapshot.Version, Convert.ToBase64String(challenge));
        return OperationResultFactory.Success(new HelloWebAuthnAssertionChallenge(
            Seal(ticket), webAuthn.RelyingPartyId, challenge,
            webAuthn.UserVerificationRequired, ticket.ExpiresAt));
    }

    public async Task<OperationResult<string>> CompletePasskeyAsync(
        string accessToken, string ticket, byte[] credentialId, byte[] clientDataJson,
        byte[] authenticatorData, byte[] signature, string? clientKey,
        CancellationToken cancellationToken)
    {
        var (user, snapshot) = await Context(accessToken, cancellationToken);
        if (user is null || snapshot is null || snapshot.HasPassword || passkeys is null)
        {
            return Fail<string>();
        }

        var payload = Read(ticket, "passkey", accessToken, user.Id, snapshot.Version);
        if (payload?.Data is null || !await Spend(payload, cancellationToken))
        {
            return Fail<string>();
        }

        var verified = await passkeys.AuthenticateAsync(
            new AuthenticateWebAuthnCommand(
                credentialId, clientDataJson, authenticatorData, signature,
                Convert.FromBase64String(payload.Data), clientKey), cancellationToken);
        if (!verified.IsSuccess)
        {
            return OperationResultFactory.Fail<string>(verified.Errors);
        }

        var current = await Context(accessToken, cancellationToken);
        if (current.User?.Id != user.Id || current.Methods?.Version != snapshot.Version)
        {
            return Fail<string>();
        }

        return verified.Value.Id == user.Id
            ? OperationResultFactory.Success(Seal(New("proof", accessToken, user.Id, snapshot.Version)))
            : Fail<string>();
    }

    public async Task<OperationResult<string>> BeginExternalAsync(
        string accessToken,
        string provider,
        string? clientKey,
        CancellationToken cancellationToken)
    {
        var (user, snapshot) = await Context(accessToken, cancellationToken);
        if (user is null
            || snapshot is null
            || snapshot.HasPassword
            || !snapshot.ExternalLogins.Any(x => string.Equals(
                x.Login.Provider, provider, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail<string>();
        }

        return OperationResultFactory.Success(Seal(New("external", accessToken, user.Id, snapshot.Version, provider)));
    }

    public async Task<OperationResult<string>> CompleteExternalAsync(
        string accessToken,
        string ticket,
        ExternalLoginKey login,
        string? clientKey,
        CancellationToken cancellationToken)
    {
        var (user, snapshot) = await Context(accessToken, cancellationToken);
        if (user is null || snapshot is null || snapshot.HasPassword)
        {
            return Fail<string>();
        }

        var payload = Read(ticket, "external", accessToken, user.Id, snapshot.Version);
        // The adapter must validate the provider's protocol response and
        // freshness; this layer independently checks exact linked identity.
        if (payload is null
            || !string.Equals(payload.Data, login.Provider, StringComparison.OrdinalIgnoreCase)
            || !snapshot.ExternalLogins.Any(x =>
                string.Equals(x.Login.Provider, login.Provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Login.Subject, login.Subject, StringComparison.Ordinal))
            || !await Spend(payload, cancellationToken))
        {
            return Fail<string>();
        }

        return OperationResultFactory.Success(Seal(New("proof", accessToken, user.Id, snapshot.Version)));
    }
}

internal sealed class HelloTotpPasswordAttempts(TimeProvider time)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, (DateTimeOffset Until, int Count)> hits = [];
    public bool TryHit(string key, int limit)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            foreach (var expired in hits.Where(x => x.Value.Until <= now).Select(x => x.Key).ToArray())
            {
                hits.Remove(expired);
            }

            if (!hits.TryGetValue(key, out var item))
            {
                if (hits.Count >= 100_000)
                {
                    return false;
                }

                hits.Add(key, (now.AddMinutes(5), 1));
                return true;
            }
            if (item.Count >= limit)
            {
                return false;
            }

            hits[key] = (item.Until, item.Count + 1);
            return true;
        }
    }
}
