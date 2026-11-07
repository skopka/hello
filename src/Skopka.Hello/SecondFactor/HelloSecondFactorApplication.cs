using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.WebAuthn;
using Skopka.Identity;
using Skopka.Identity.Authentication;
using Skopka.Identity.RateLimiting;
using Skopka.Identity.Sessions;
using Skopka.Identity.Totp;
using Skopka.Identity.Verification;

namespace Skopka.Hello;

internal sealed record HelloSecondFactorTicket(Guid FlowId, Guid UserId, string Stamp,
    IdentitySessionMetadata? Metadata, string? ClientKey, DateTimeOffset ExpiresAt,
    string Stage, Guid ChallengeId = default)
{
    public IdentitySessionContext? Context { get; init; }
    public override string ToString() => "[protected second-factor state]";
}

internal sealed class HelloSecondFactorTickets(IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("Skopka.Hello.SecondFactor.v1");
    internal string Seal(HelloSecondFactorTicket value) => protector.Protect(JsonSerializer.Serialize(value));
    internal HelloSecondFactorTicket? Read(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8192) return null;
        try
        {
            var ticket = JsonSerializer.Deserialize<HelloSecondFactorTicket>(protector.Unprotect(value));
            return ticket is not null && ticket.FlowId != Guid.Empty && ticket.UserId != Guid.Empty
                && !string.IsNullOrWhiteSpace(ticket.Stamp) && ticket.ExpiresAt > clock.GetUtcNow()
                ? ticket : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException) { return null; }
    }
    internal HelloSecondFactorTicket New(Guid userId, string stamp, IdentitySessionMetadata? metadata, string? clientKey,
        IdentitySessionContext? context = null)
        => new(Guid.NewGuid(), userId, stamp, metadata, clientKey, clock.GetUtcNow().AddMinutes(5), "primary") { Context = context };
    internal static HelloSecondFactorTicket Next(HelloSecondFactorTicket ticket, string stage, Guid challengeId = default)
        => ticket with { FlowId = Guid.NewGuid(), Stage = stage, ChallengeId = challengeId };
}

internal sealed class HelloSecondFactorAdmission<TProfile>(
    IEnumerable<IHelloSignInSecondFactorPolicy<TProfile>> policies,
    HelloSecondFactorTickets tickets,
    IHttpContextAccessor http,
    IHelloRequestContext requestContext,
    SkopkaHelloOptions options,
    IEnumerable<IIdentityRateLimiter<TProfile>> limiters,
    IIdentityTotpService<TProfile>? totp = null) : IIdentitySessionAdmissionPolicy<TProfile>
{
    public async Task<OperationResult> CheckAsync(IdentitySessionAdmissionContext<TProfile> context, CancellationToken ct)
    {
        foreach (var policy in policies)
        {
            if (!await policy.RequiresAsync(context.User, ct)) continue;
            if (context.Assurance == IdentitySessionAssurance.TotpVerified) continue;
            if (context.Operation != IdentitySessionAdmissionOperation.Create || !options.Totp.Enabled || totp is null || !limiters.Any())
                return OperationResultFactory.Fail(new Error(HelloSecondFactorErrorCodes.Required,
                    "Fresh authentication with an authenticator is required.", ErrorType.Unauthorized));
            var clientKey = http.HttpContext is { } current ? requestContext.CreateClientKey(current) : null;
            var value = tickets.New(context.User.Id, context.User.SecurityStamp, context.Metadata, clientKey, context.Context);
            return OperationResultFactory.Fail(new Error(HelloSecondFactorErrorCodes.Required,
                "Authenticator verification is required.", ErrorType.Unauthorized,
                new HelloSecondFactorRequired(tickets.Seal(value), value.ExpiresAt)));
        }
        return OperationResultFactory.Success();
    }
}

internal sealed class HelloSecondFactorApplication<TProfile>(
    HelloSecondFactorTickets tickets,
    IHelloWebAuthnFlowStore flows,
    IIdentityVerificationService<TProfile> verification,
    IIdentitySessionService<TProfile> sessions,
    IIdentityUserLookupService<TProfile> users,
    SkopkaHelloOptions options,
    IIdentityTotpService<TProfile>? totp = null,
    IHelloSiteContext? siteContext = null) : IHelloSecondFactorApplication<TProfile>
{
    private const string Purpose = "hello.signin.second_factor";
    private static string Binding(HelloSecondFactorTicket ticket) => ticket.FlowId.ToString("N");
    private static Error Invalid() => new(HelloSecondFactorErrorCodes.Invalid, "The sign-in continuation is invalid or expired.", ErrorType.Unauthorized);
    private static OperationResult<T> Fail<T>() => OperationResultFactory.Fail<T>(Invalid());
    private async Task<bool> Spend(HelloSecondFactorTicket ticket, CancellationToken ct)
        => await flows.TryConsumeAsync(ticket.FlowId, ticket.ExpiresAt, ct);
    private async Task<bool> Current(HelloSecondFactorTicket ticket, CancellationToken ct)
    {
        if (siteContext is not null && (siteContext.Current is not { } site
            || ticket.Context?.Scope != site.Id
            || !HelloSiteAccess.AllowsMethod(site, ticket.Context.AuthenticationMethod))) return false;
        var result = await users.FindActiveByIdAsync(ticket.UserId, ct);
        var user = result.IsSuccess ? result.Value : null;
        return user is not null && user.SecurityStamp == ticket.Stamp
            && (user.BlockedAt is null || (user.BlockedUntil is not null && user.BlockedUntil <= DateTimeOffset.UtcNow));
    }
    public async Task<OperationResult<HelloSecondFactorStatus>> InspectAsync(string ticket, CancellationToken cancellationToken)
    {
        var state = tickets.Read(ticket);
        if (state is null || !await Current(state, cancellationToken) || totp is null) return Fail<HelloSecondFactorStatus>();
        if (state.Stage == "ack") return OperationResultFactory.Success(new HelloSecondFactorStatus(HelloSecondFactorStage.Acknowledge, state.ExpiresAt));
        var status = await totp.GetStatusAsync(state.UserId, cancellationToken);
        if (!status.IsSuccess) return OperationResultFactory.Fail<HelloSecondFactorStatus>(status.Errors);
        return OperationResultFactory.Success(new HelloSecondFactorStatus(status.Value.IsEnabled ? HelloSecondFactorStage.Verify : HelloSecondFactorStage.Enroll, state.ExpiresAt));
    }
    public async Task<OperationResult<HelloSecondFactorContinuation>> BeginVerificationAsync(string ticket, string? clientKey, CancellationToken cancellationToken)
    {
        var state = tickets.Read(ticket);
        if (state is null || state.Stage != "primary" || !await Current(state, cancellationToken) || !await Spend(state, cancellationToken)) return Fail<HelloSecondFactorContinuation>();
        var next = HelloSecondFactorTickets.Next(state, "verify");
        var begun = await verification.BeginAsync(new BeginVerificationCommand(state.UserId, Purpose, Binding(next), VerificationMethods.TimeBasedOneTimePassword, clientKey ?? state.ClientKey), cancellationToken);
        if (!begun.IsSuccess) return OperationResultFactory.Fail<HelloSecondFactorContinuation>(begun.Errors);
        next = next with { ChallengeId = begun.Value.ChallengeId };
        return OperationResultFactory.Success(new HelloSecondFactorContinuation(tickets.Seal(next), next.ExpiresAt));
    }
    public async Task<OperationResult<HelloSignIn<TProfile>>> CompleteVerificationAsync(string ticket, string code, string? clientKey, CancellationToken cancellationToken)
    {
        var state = tickets.Read(ticket);
        if (state is null || state.Stage != "verify" || !await Current(state, cancellationToken)) return Fail<HelloSignIn<TProfile>>();
        var verified = await verification.VerifyAsync(new VerifyVerificationChallengeCommand(state.ChallengeId, state.UserId, code, clientKey ?? state.ClientKey), cancellationToken);
        if (!verified.IsSuccess) return OperationResultFactory.Fail<HelloSignIn<TProfile>>(verified.Errors);
        if (!await Spend(state, cancellationToken)) return Fail<HelloSignIn<TProfile>>();
        var consumed = await verification.ConsumeAsync(new ConsumeVerificationProofCommand(state.ChallengeId, state.UserId, Purpose, Binding(state), verified.Value.Token), cancellationToken);
        if (!consumed.IsSuccess) return OperationResultFactory.Fail<HelloSignIn<TProfile>>(consumed.Errors);
        return await Issue(state, cancellationToken);
    }
    public async Task<OperationResult<HelloSecondFactorEnrollment>> BeginEnrollmentAsync(string ticket, string? clientKey, CancellationToken cancellationToken)
    {
        var state = tickets.Read(ticket);
        if (state is null || state.Stage != "primary" || totp is null || !await Current(state, cancellationToken) || !await Spend(state, cancellationToken)) return Fail<HelloSecondFactorEnrollment>();
        var begun = await totp.BeginEnrollmentAsync(new BeginTotpEnrollmentCommand(state.UserId, clientKey ?? state.ClientKey), cancellationToken);
        if (!begun.IsSuccess) return OperationResultFactory.Fail<HelloSecondFactorEnrollment>(begun.Errors);
        var next = HelloSecondFactorTickets.Next(state, "enroll", begun.Value.EnrollmentId);
        var found = await users.FindActiveByIdAsync(state.UserId, cancellationToken);
        if (!found.IsSuccess || found.Value.SecurityStamp != state.Stamp) return Fail<HelloSecondFactorEnrollment>();
        var user = found.Value;
        var label = user.Email ?? user.UserName ?? user.Phone ?? user.Id.ToString("D");
        var issuer = options.Totp.Issuer;
        var uri = HelloIdentityApplication<TProfile>.CreateProvisioningUri(issuer, label, begun.Value.Secret);
        return OperationResultFactory.Success(new HelloSecondFactorEnrollment(tickets.Seal(next), begun.Value.Secret, uri, next.ExpiresAt));
    }
    public async Task<OperationResult<HelloSecondFactorRecovery>> ConfirmEnrollmentAsync(string ticket, string code, string? clientKey, CancellationToken cancellationToken)
    {
        var state = tickets.Read(ticket);
        if (state is null || state.Stage != "enroll" || totp is null || !await Current(state, cancellationToken)) return Fail<HelloSecondFactorRecovery>();
        var confirmed = await totp.ConfirmEnrollmentAsync(new ConfirmTotpEnrollmentCommand(state.UserId, state.ChallengeId, code, clientKey ?? state.ClientKey), cancellationToken);
        if (!confirmed.IsSuccess) return OperationResultFactory.Fail<HelloSecondFactorRecovery>(confirmed.Errors);
        if (!await Spend(state, cancellationToken)) return Fail<HelloSecondFactorRecovery>();
        var next = HelloSecondFactorTickets.Next(state, "ack", state.ChallengeId);
        return OperationResultFactory.Success(new HelloSecondFactorRecovery(tickets.Seal(next), confirmed.Value.RecoveryCodes, next.ExpiresAt));
    }
    public async Task<OperationResult<HelloSignIn<TProfile>>> AcknowledgeEnrollmentAsync(string ticket, CancellationToken cancellationToken)
    {
        var state = tickets.Read(ticket);
        if (state is null || state.Stage != "ack" || !await Current(state, cancellationToken) || !await Spend(state, cancellationToken)) return Fail<HelloSignIn<TProfile>>();
        return await Issue(state, cancellationToken);
    }
    private async Task<OperationResult<HelloSignIn<TProfile>>> Issue(HelloSecondFactorTicket state, CancellationToken ct)
    {
        var issued = await sessions.CreateAsync(new CreateIdentitySessionCommand(state.UserId, state.Stamp, state.Metadata)
        { Assurance = IdentitySessionAssurance.TotpVerified, Context = state.Context }, ct);
        if (!issued.IsSuccess) return OperationResultFactory.Fail<HelloSignIn<TProfile>>(issued.Errors);
        var user = await sessions.ValidateAccessTokenAsync(issued.Value.AccessToken, ct);
        if (!user.IsSuccess)
        {
            await sessions.RevokeByIdAsync(new RevokeIdentitySessionByIdCommand(state.UserId, issued.Value.SessionId), ct);
            return OperationResultFactory.Fail<HelloSignIn<TProfile>>(user.Errors);
        }
        var u = user.Value;
        var account = new HelloAccount<TProfile>(u.Id, u.Flags, u.UserName, u.Email, u.EmailConfirmed, u.Phone, u.PhoneConfirmed, u.Profile, u.Version, u.CreatedAt, u.ModifiedAt);
        var s = issued.Value;
        return OperationResultFactory.Success(new HelloSignIn<TProfile>(account, new HelloSession(s.SessionId, s.AccessToken, s.AccessTokenExpiresAt, s.RefreshToken, s.RefreshTokenExpiresAt)));
    }
}

