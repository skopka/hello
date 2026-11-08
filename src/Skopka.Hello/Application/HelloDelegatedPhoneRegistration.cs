using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Authentication;
using Skopka.Identity.Errors;
using Skopka.Identity.PhoneOtp;
using Skopka.Identity.Registration;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Commands;
using Skopka.Identity.Verification;

namespace Skopka.Hello;

/// <summary>Host policy authorizing an actor to act on a phone number that is not theirs.</summary>
/// <remarks>It gates both delegated registration and delegated phone verification; with no policy both are denied.</remarks>
public interface IHelloDelegatedPhoneRegistrationPolicy<TProfile>
{
    /// <summary>Allows or rejects this actor; all registered policies must allow the operation.</summary>
    Task<OperationResult> AuthorizeAsync(HelloAccount<TProfile> actor, CancellationToken cancellationToken);
}

/// <summary>Starts and completes server-side delegated phone registration without changing the actor session.</summary>
public interface IHelloDelegatedPhoneRegistration<TProfile>
{
    /// <summary>Sends a code through the configured SMS channel and returns an actor-session-bound ticket.</summary>
    Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string accessToken, string phone, string? clientKey, CancellationToken cancellationToken);
    /// <summary>Starts verification with a response for configured pre-send CAPTCHA policies.</summary>
    Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string accessToken, string phone, string? clientKey, string? captchaResponse, CancellationToken cancellationToken);
    /// <summary>Verifies the code and atomically registers a separate confirmed-phone account without changing the actor's session.</summary>
    Task<OperationResult<HelloAccount<TProfile>>> CompleteAsync(string accessToken, string ticket, string code, string? clientKey,
        TProfile profile, bool acceptTermsOfService, bool acceptPrivacyPolicy, CancellationToken cancellationToken);
}

/// <summary>
/// Lets an authenticated actor prove possession of a phone number that may belong to another account, and lets
/// the host either register a new account on that number or redeem the proof to link an existing one.
/// </summary>
/// <remarks>
/// Codes are sent exactly as for delegated registration, so SMS budgets, limits and the verification provider
/// apply unchanged, and tickets from either service are interchangeable. Whether the number is in use is
/// disclosed only after the correct code. The proof is bound to the actor and its access token, expires with the
/// Skopka.Identity verification proof (<c>VerificationOptions.ProofLifetime</c>, two minutes by default) and is
/// spent once, atomically in the Identity store, by either <see cref="RegisterAsync"/> or <see cref="RedeemAsync"/>.
/// The same <see cref="IHelloDelegatedPhoneRegistrationPolicy{TProfile}"/> authorizes every step.
/// </remarks>
public interface IHelloDelegatedPhoneVerification<TProfile>
{
    /// <summary>Sends a code through the configured SMS channel and returns an actor-session-bound ticket.</summary>
    Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string accessToken, string phone, string? clientKey, CancellationToken cancellationToken);
    /// <summary>Starts verification with a response for configured pre-send CAPTCHA policies.</summary>
    Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string accessToken, string phone, string? clientKey, string? captchaResponse, CancellationToken cancellationToken);
    /// <summary>Verifies the code and returns an unspent proof plus the account that holds the confirmed number, if any.</summary>
    /// <remarks>Identity verification errors are returned unchanged; a wrong code reveals nothing about the number.</remarks>
    Task<OperationResult<HelloPhoneOwnershipProof>> VerifyAsync(string accessToken, string ticket, string code, string? clientKey, CancellationToken cancellationToken);
    /// <summary>Spends the proof and returns the trusted ownership fact; the holder is read again at redemption.</summary>
    Task<OperationResult<HelloPhoneOwnership>> RedeemAsync(string accessToken, string proof, CancellationToken cancellationToken);
    /// <summary>Spends the proof and atomically registers a separate confirmed-phone account without changing the actor's session.</summary>
    /// <remarks>Fails with <c>DuplicatePhone</c> when the number is in use; consent, admission and profile hooks apply as in delegated registration.</remarks>
    Task<OperationResult<HelloAccount<TProfile>>> RegisterAsync(string accessToken, string proof, TProfile profile,
        bool acceptTermsOfService, bool acceptPrivacyPolicy, CancellationToken cancellationToken);
}

/// <summary>Result of a correct code: an opaque proof for the actor's session and the current holder of the number.</summary>
/// <param name="Proof">Protected one-use value; keep it in the actor's server-side flow and never log it.</param>
/// <param name="Phone">The normalized verified number.</param>
/// <param name="VerifiedAt">When the code was accepted.</param>
/// <param name="ExpiresAt">When the proof can no longer be spent.</param>
/// <param name="AccountId">The active account whose confirmed phone is this number, or <see langword="null"/>; it may be the actor.</param>
public sealed record HelloPhoneOwnershipProof(string Proof, string Phone, DateTimeOffset VerifiedAt, DateTimeOffset ExpiresAt, Guid? AccountId);

/// <summary>Trusted, already spent fact that <paramref name="ActorId"/> proved possession of <paramref name="Phone"/> at <paramref name="VerifiedAt"/>.</summary>
/// <param name="ActorId">The authenticated account that entered the code.</param>
/// <param name="Phone">The normalized verified number.</param>
/// <param name="VerifiedAt">When the code was accepted.</param>
/// <param name="AccountId">The active account whose confirmed phone is this number at redemption, or <see langword="null"/>; it may be the actor.</param>
public sealed record HelloPhoneOwnership(Guid ActorId, string Phone, DateTimeOffset VerifiedAt, Guid? AccountId);

internal sealed class HelloDelegatedPhoneRegistration<TProfile>(
    IHelloPhoneOtpApplication<TProfile> sender, IIdentityPhoneOtpService<TProfile> phoneOtp,
    IIdentityRegistrationService<TProfile> registration, IEnumerable<IHelloAccessTokenValidator<TProfile>> validators,
    IEnumerable<IHelloDelegatedPhoneRegistrationPolicy<TProfile>> policies, HelloPhoneInputPolicy phonePolicy,
    IHelloRegistrationConsentPolicy consentPolicy, HelloRegistrationAdmission<TProfile> admission,
    IDataProtectionProvider protection, TimeProvider clock,
    IIdentityVerificationService<TProfile> verification, IIdentityUserLookupService<TProfile> lookup,
    IHelloRegistrationConsentProfileEnricher<TProfile>? enricher = null,
    IHelloRegistrationContinuation<TProfile>? continuation = null,
    IHelloSiteContext? sites = null)
    : IHelloDelegatedPhoneRegistration<TProfile>, IHelloDelegatedPhoneVerification<TProfile>
{
    private const int MaximumProtectedLength = 8192;
    private readonly IDataProtector protector = protection.CreateProtector("Skopka.Hello.DelegatedPhoneRegistration.v1");
    private readonly IDataProtector proofProtector = protection.CreateProtector("Skopka.Hello.DelegatedPhoneVerification.Proof.v1");
    private sealed record Ticket(Guid ActorId, string TokenHash, string Phone, Guid ChallengeId, DateTimeOffset ExpiresAt);
    private sealed record Proof(Guid ActorId, string TokenHash, string Phone, Guid ChallengeId, string Token,
        DateTimeOffset VerifiedAt, DateTimeOffset ExpiresAt);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static Error Invalid() => new(IdentityErrorCodes.VerificationProofInvalid, "Phone registration request is invalid or expired.", ErrorType.Unauthorized);
    private static HelloAccount<TProfile> Account(IdentityUser<TProfile> u) => new(u.Id, u.Flags, u.UserName, u.Email, u.EmailConfirmed, u.Phone, u.PhoneConfirmed, u.Profile, u.Version, u.CreatedAt, u.ModifiedAt);

    private async Task<OperationResult<HelloAccount<TProfile>>> AuthorizeAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return OperationResultFactory.Fail<HelloAccount<TProfile>>(Invalid());
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(token, ct);
            if (!result.IsSuccess) continue;
            var actor = Account(result.Value);
            var registeredPolicies = policies.ToArray();
            if (registeredPolicies.Length == 0) break;
            foreach (var policy in registeredPolicies)
            {
                var authorized = await policy.AuthorizeAsync(actor, ct);
                if (!authorized.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(authorized.Errors);
            }
            return OperationResultFactory.Success(actor);
        }
        return OperationResultFactory.Fail<HelloAccount<TProfile>>(Invalid());
    }

    private static T? Read<T>(IDataProtector source, string value) where T : class
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) || value.Length > MaximumProtectedLength
                ? null : JsonSerializer.Deserialize<T>(source.Unprotect(value));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private Ticket? ReadTicket(string ticket, Guid actorId, string accessToken)
    {
        var pending = Read<Ticket>(protector, ticket);
        return pending is null || pending.ExpiresAt <= clock.GetUtcNow() || pending.ActorId != actorId || pending.TokenHash != Hash(accessToken)
            ? null : pending;
    }

    private Proof? ReadProof(string proof, Guid actorId, string accessToken)
    {
        var pending = Read<Proof>(proofProtector, proof);
        return pending is null || pending.ExpiresAt <= clock.GetUtcNow() || pending.ActorId != actorId || pending.TokenHash != Hash(accessToken)
            || string.IsNullOrEmpty(pending.Token) || string.IsNullOrEmpty(pending.Phone)
            ? null : pending;
    }

    public Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string accessToken, string phone, string? clientKey, CancellationToken cancellationToken)
        => BeginAsync(accessToken, phone, clientKey, null, cancellationToken);

    public async Task<OperationResult<HelloPhoneBindingChallenge>> BeginAsync(string accessToken, string phone, string? clientKey, string? captchaResponse, CancellationToken cancellationToken)
    {
        var actor = await AuthorizeAsync(accessToken, cancellationToken);
        if (!actor.IsSuccess) return OperationResultFactory.Fail<HelloPhoneBindingChallenge>(actor.Errors);
        var normalized = phonePolicy.Normalize(phone, otpRequest: true);
        if (!normalized.IsSuccess) return OperationResultFactory.Fail<HelloPhoneBindingChallenge>(normalized.Errors);
        var issued = await sender.RequestCodeAsync(normalized.Value, clientKey, captchaResponse, cancellationToken);
        if (!issued.IsSuccess) return OperationResultFactory.Fail<HelloPhoneBindingChallenge>(issued.Errors);
        var ticket = new Ticket(actor.Value.Id, Hash(accessToken), normalized.Value, issued.Value.RequestId, clock.GetUtcNow().AddMinutes(5));
        return OperationResultFactory.Success(new HelloPhoneBindingChallenge(protector.Protect(JsonSerializer.Serialize(ticket)), issued.Value.RetryAfter));
    }

    public async Task<OperationResult<HelloAccount<TProfile>>> CompleteAsync(string accessToken, string ticket, string code, string? clientKey,
        TProfile profile, bool acceptTermsOfService, bool acceptPrivacyPolicy, CancellationToken cancellationToken)
    {
        var actor = await AuthorizeAsync(accessToken, cancellationToken);
        if (!actor.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(actor.Errors);
        var pending = ReadTicket(ticket, actor.Value.Id, accessToken);
        if (pending is null) return OperationResultFactory.Fail<HelloAccount<TProfile>>(Invalid());
        var prepared = await PrepareRegistrationAsync(profile, acceptTermsOfService, acceptPrivacyPolicy, cancellationToken);
        if (!prepared.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(prepared.Errors);
        var proof = await phoneOtp.VerifyForBindingAsync(new VerifyPhoneOtpCommand(pending.ChallengeId, code, clientKey), cancellationToken);
        if (!proof.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(proof.Errors);
        return await RegisterCoreAsync(pending.Phone, prepared.Value, proof.Value, cancellationToken);
    }

    public async Task<OperationResult<HelloPhoneOwnershipProof>> VerifyAsync(string accessToken, string ticket, string code,
        string? clientKey, CancellationToken cancellationToken)
    {
        var actor = await AuthorizeAsync(accessToken, cancellationToken);
        if (!actor.IsSuccess) return OperationResultFactory.Fail<HelloPhoneOwnershipProof>(actor.Errors);
        var pending = ReadTicket(ticket, actor.Value.Id, accessToken);
        if (pending is null) return OperationResultFactory.Fail<HelloPhoneOwnershipProof>(Invalid());
        var verified = await phoneOtp.VerifyForBindingAsync(new VerifyPhoneOtpCommand(pending.ChallengeId, code, clientKey), cancellationToken);
        if (!verified.IsSuccess) return OperationResultFactory.Fail<HelloPhoneOwnershipProof>(verified.Errors);
        // Looked up only now, after the correct code: until then a taken and a free number look the same.
        var holder = await FindHolderAsync(pending.Phone, cancellationToken);
        if (!holder.IsSuccess) return OperationResultFactory.Fail<HelloPhoneOwnershipProof>(holder.Errors);
        var now = clock.GetUtcNow();
        var payload = new Proof(actor.Value.Id, pending.TokenHash, pending.Phone, verified.Value.ChallengeId, verified.Value.Token,
            now, verified.Value.ExpiresAt);
        return OperationResultFactory.Success(new HelloPhoneOwnershipProof(
            proofProtector.Protect(JsonSerializer.Serialize(payload)), pending.Phone, now, verified.Value.ExpiresAt, holder.Value));
    }

    public async Task<OperationResult<HelloPhoneOwnership>> RedeemAsync(string accessToken, string proof, CancellationToken cancellationToken)
    {
        var actor = await AuthorizeAsync(accessToken, cancellationToken);
        if (!actor.IsSuccess) return OperationResultFactory.Fail<HelloPhoneOwnership>(actor.Errors);
        var pending = ReadProof(proof, actor.Value.Id, accessToken);
        if (pending is null) return OperationResultFactory.Fail<HelloPhoneOwnership>(Invalid());
        // Identity spends the proof atomically in its store, so a second redemption fails on every instance.
        var consumed = await verification.ConsumeAsync(new ConsumeVerificationProofCommand(pending.ChallengeId, Guid.Empty,
            VerificationPurposes.PhoneSignIn, pending.Phone, pending.Token), cancellationToken);
        if (!consumed.IsSuccess) return OperationResultFactory.Fail<HelloPhoneOwnership>(consumed.Errors);
        var holder = await FindHolderAsync(pending.Phone, cancellationToken);
        if (!holder.IsSuccess) return OperationResultFactory.Fail<HelloPhoneOwnership>(holder.Errors);
        return OperationResultFactory.Success(new HelloPhoneOwnership(actor.Value.Id, pending.Phone, pending.VerifiedAt, holder.Value));
    }

    public async Task<OperationResult<HelloAccount<TProfile>>> RegisterAsync(string accessToken, string proof, TProfile profile,
        bool acceptTermsOfService, bool acceptPrivacyPolicy, CancellationToken cancellationToken)
    {
        var actor = await AuthorizeAsync(accessToken, cancellationToken);
        if (!actor.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(actor.Errors);
        var pending = ReadProof(proof, actor.Value.Id, accessToken);
        if (pending is null) return OperationResultFactory.Fail<HelloAccount<TProfile>>(Invalid());
        var prepared = await PrepareRegistrationAsync(profile, acceptTermsOfService, acceptPrivacyPolicy, cancellationToken);
        if (!prepared.IsSuccess) return OperationResultFactory.Fail<HelloAccount<TProfile>>(prepared.Errors);
        return await RegisterCoreAsync(pending.Phone, prepared.Value,
            new VerificationProof(pending.ChallengeId, pending.Token, pending.ExpiresAt), cancellationToken);
    }

    private async Task<OperationResult<TProfile>> PrepareRegistrationAsync(TProfile profile, bool acceptTermsOfService,
        bool acceptPrivacyPolicy, CancellationToken cancellationToken)
    {
        var consent = consentPolicy.Validate(new HelloRegistrationConsent(acceptTermsOfService, acceptPrivacyPolicy,
            acceptTermsOfService || acceptPrivacyPolicy ? clock.GetUtcNow() : null));
        if (!consent.IsSuccess) return OperationResultFactory.Fail<TProfile>(consent.Errors);
        if (enricher is not null)
        {
            var enriched = enricher.Enrich(profile, consent.Value);
            if (!enriched.IsSuccess) return OperationResultFactory.Fail<TProfile>(enriched.Errors);
            profile = enriched.Value;
        }
        if (continuation is not null)
        {
            var prepared = await continuation.PrepareProfileAsync(profile, HelloRegistrationKind.DelegatedPhoneOtp, cancellationToken);
            if (!prepared.IsSuccess) return OperationResultFactory.Fail<TProfile>(prepared.Errors);
            profile = prepared.Value;
        }
        var admitted = await admission.CheckAsync(HelloRegistrationKind.DelegatedPhoneOtp, cancellationToken);
        return admitted.IsSuccess ? OperationResultFactory.Success(profile) : OperationResultFactory.Fail<TProfile>(admitted.Errors);
    }

    private async Task<OperationResult<HelloAccount<TProfile>>> RegisterCoreAsync(string phone, TProfile profile,
        VerificationProof proof, CancellationToken cancellationToken)
    {
        var created = await registration.RegisterVerifiedPhoneAsync(new RegisterVerifiedPhoneUserCommand<TProfile>(
            new CreateUserCommand<TProfile>(null, null, phone, profile) { PhoneScope = HelloSiteAccess.PhoneScope(sites) },
            proof), cancellationToken);
        return created.IsSuccess ? OperationResultFactory.Success(Account(created.Value)) : OperationResultFactory.Fail<HelloAccount<TProfile>>(created.Errors);
    }

    // The holder is the active account whose phone is this number and confirmed: an unconfirmed number says nothing
    // about who owns that account, and a deleted account is not active. A blocked account still counts; the host decides.
    private async Task<OperationResult<Guid?>> FindHolderAsync(string phone, CancellationToken cancellationToken)
    {
        // Only the holder in this site's phone scope: a staff account with the same number is not reported on the main site.
        var found = await lookup.FindActiveByPhoneAsync(phone, HelloSiteAccess.PhoneScope(sites), cancellationToken);
        if (found.IsSuccess) return OperationResultFactory.Success<Guid?>(found.Value.PhoneConfirmed ? found.Value.Id : null);
        return found.Errors.All(error => error.Code == IdentityErrorCodes.UserNotFound)
            ? OperationResultFactory.Success<Guid?>(null)
            : OperationResultFactory.Fail<Guid?>(found.Errors);
    }
}
